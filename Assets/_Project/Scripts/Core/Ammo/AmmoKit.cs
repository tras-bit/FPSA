using System;
using System.Collections.Generic;
using Kordon.Core.Data;
using Kordon.Core.Loadout;
using Kordon.Core.Utility;

namespace Kordon.Core.Ammo
{
    /// <summary>Result of a trigger pull.</summary>
    public struct FireAttempt
    {
        public bool Fired;
        public FireDenial Denial;
        public int RoundsLeftInMagazine;
        public bool WasLastRound;

        public static FireAttempt Denied(FireDenial denial) => new FireAttempt { Fired = false, Denial = denial };

        public static FireAttempt Success(int roundsLeft, bool wasLastRound) => new FireAttempt
        {
            Fired = true,
            RoundsLeftInMagazine = roundsLeft,
            WasLastRound = wasLastRound
        };
    }

    /// <summary>
    /// Everything the HUD may know about ammunition at a given moment.
    ///
    /// There is deliberately no total round counter and no exact magazine count:
    /// <see cref="InspectedRounds"/> is only non-null while the operator is holding R
    /// and physically looking at the magazine.  This is the data contract the UI is
    /// allowed to bind to (see docs/03_MAGAZINE_SYSTEM.md).
    /// </summary>
    public sealed class AmmoHudSnapshot
    {
        public List<MagazineHudSlot> Slots = new List<MagazineHudSlot>();
        public bool IsInspecting;
        public int? InspectedRounds;
        public int? InspectedCapacity;
        public string InspectedMagazineName = string.Empty;
        public bool WeaponIsEmpty;
        public bool HasReserveAmmunition;
        public AmmoInteraction Interaction;
        public ReloadType Reload;
        public float ReloadProgress01;
        public string WeaponId = string.Empty;
        public int GrenadesRemaining;

        /// <summary>True when the exact round count may be rendered right now.</summary>
        public bool MayShowExactCount => IsInspecting && InspectedRounds.HasValue;
    }

    /// <summary>One scheduled animation/sound cue inside a reload or inspection.</summary>
    internal struct ScheduledAmmoEvent
    {
        public float At;
        public AmmoEventKind Kind;
        public string MagazineId;
        public bool Fired;

        public ScheduledAmmoEvent(float at, AmmoEventKind kind, string magazineId)
        {
            At = at;
            Kind = kind;
            MagazineId = magazineId;
            Fired = false;
        }
    }

    /// <summary>
    /// The magazine system for one weapon.
    ///
    /// Behaviour implemented here (all of it covered by automated tests):
    ///   * magazines are created from the pre-round plan and never change afterwards;
    ///   * short press R = reload, holding R = pull the magazine out and look at it;
    ///   * while inspecting, the weapon cannot fire;
    ///   * the exact round count is only readable during an inspection;
    ///   * the HUD strip shows every magazine as white / yellow / red;
    ///   * a partially spent magazine always returns to the pouch with its rounds;
    ///   * handling mistakes are visible, timed events - never a silent misfire.
    /// </summary>
    [Serializable]
    public sealed class AmmoKit
    {
        private readonly List<MagazineInstance> _pouch = new List<MagazineInstance>();
        private readonly List<ScheduledAmmoEvent> _schedule = new List<ScheduledAmmoEvent>();

        public WeaponDefinition Weapon { get; }
        public DefinitionLibrary Library { get; }
        public AmmoKitOptions Options { get; }
        private readonly DeterministicRandom _random;

        public MagazineInstance Chambered { get; private set; }
        public bool RoundChambered { get; private set; }
        public IReadOnlyList<MagazineInstance> Pouch => _pouch;
        public AmmoInteraction State { get; private set; } = AmmoInteraction.Idle;
        public ReloadType CurrentReloadType { get; private set; } = ReloadType.None;
        public float StateStartedAt { get; private set; }
        public float StateEndsAt { get; private set; }
        public float LastFireTime { get; private set; } = float.NegativeInfinity;
        public float HoldStartedAt { get; private set; }
        public float InspectReadableAt { get; private set; }
        public int FumbleCount { get; private set; }
        public int ReloadCount { get; private set; }
        public int InspectCount { get; private set; }
        public int RoundsFired { get; private set; }

        public List<AmmoEvent> PendingEvents { get; } = new List<AmmoEvent>();

        public AmmoKit(WeaponDefinition weapon, List<MagazinePlanEntry> plan, DefinitionLibrary library,
            AmmoKitOptions options = null, DeterministicRandom random = null)
        {
            Weapon = weapon;
            Library = library;
            Options = options ?? new AmmoKitOptions();
            _random = random ?? new DeterministicRandom(0x5EED1234UL);
            BuildFromPlan(plan);
        }

        // ------------------------------------------------------------ construction

        private void BuildFromPlan(List<MagazinePlanEntry> plan)
        {
            _pouch.Clear();
            Chambered = null;
            RoundChambered = false;

            if (plan == null)
            {
                return;
            }

            foreach (MagazinePlanEntry entry in plan)
            {
                MagazineDefinition definition = Library?.GetMagazine(entry.MagazineId);
                if (definition == null)
                {
                    continue;
                }

                for (int i = 0; i < entry.Count; i++)
                {
                    _pouch.Add(new MagazineInstance(definition, definition.Capacity));
                }
            }

            // One magazine starts inside the weapon.
            if (_pouch.Count > 0)
            {
                Chambered = _pouch[0];
                _pouch.RemoveAt(0);
                RoundChambered = Chambered.Rounds > 0;
            }
        }

        /// <summary>
        /// Re-issue the locked kit with full magazines (respawn resupply).  The magazine
        /// types and counts never change - only the round counts are restored, and only
        /// when <see cref="AmmoKitOptions.Resupply"/> allows it.
        /// </summary>
        public bool ReissueFullMagazines()
        {
            if (Options.Resupply == ResupplyRule.NeverWithinRound)
            {
                return false;
            }

            Chambered?.Refill();
            foreach (MagazineInstance magazine in _pouch)
            {
                magazine.Refill();
            }

            RoundChambered = Chambered != null && Chambered.Rounds > 0;
            CancelInteraction();
            return true;
        }

        /// <summary>Refuse any resupply: used for the "no resupply between respawns" preset.</summary>
        public void SuppressResupply() => Options.Resupply = ResupplyRule.NeverWithinRound;

        // ------------------------------------------------------------ queries

        public bool IsBusy => State != AmmoInteraction.Idle;
        public bool IsInspecting => State == AmmoInteraction.Inspecting ||
                                    State == AmmoInteraction.ReturningFromInspect;
        public bool MayShowExactCount => State == AmmoInteraction.Inspecting;

        public bool HasReserveAmmunition
        {
            get
            {
                foreach (MagazineInstance magazine in _pouch)
                {
                    if (magazine.Rounds > 0)
                    {
                        return true;
                    }
                }

                return false;
            }
        }

        public int MagazineCount => _pouch.Count + (Chambered != null ? 1 : 0);

        public int TotalRoundsServerOnly
        {
            get
            {
                // Server side bookkeeping only (match stats, debugging).  The HUD must
                // use AmmoHudSnapshot, which never exposes this number.
                int total = Chambered?.Rounds ?? 0;
                foreach (MagazineInstance magazine in _pouch)
                {
                    total += magazine.Rounds;
                }

                return total;
            }
        }

        /// <summary>
        /// The exact count of rounds in the magazine currently inside the weapon.
        /// Returns null unless the operator is physically inspecting it.
        /// </summary>
        public int? InspectedRoundCount
        {
            get
            {
                if (!MayShowExactCount || Chambered == null)
                {
                    return null;
                }

                return Chambered.Rounds;
            }
        }

        public string InspectedMagazineName =>
            MayShowExactCount && Chambered != null
                ? Library?.GetMagazine(Chambered.DefinitionId)?.DisplayName ?? Chambered.DefinitionId
                : string.Empty;

        // ------------------------------------------------------------ firing

        /// <summary>Attempt to fire. The server calls this with its own authoritative clock.</summary>
        public FireAttempt TryFire(float now, bool sprinting = false, bool matchAllows = true)
        {
            if (!matchAllows)
            {
                return FireAttempt.Denied(FireDenial.NotAllowedByMatch);
            }

            switch (State)
            {
                case AmmoInteraction.Reloading: return FireAttempt.Denied(FireDenial.Reloading);
                case AmmoInteraction.Inspecting: return FireAttempt.Denied(FireDenial.Inspecting);
                case AmmoInteraction.ReturningFromInspect: return FireAttempt.Denied(FireDenial.Inspecting);
                case AmmoInteraction.Chambering: return FireAttempt.Denied(FireDenial.Chambering);
                case AmmoInteraction.FumbleRecovery: return FireAttempt.Denied(FireDenial.Fumbling);
                case AmmoInteraction.PendingDecision: return FireAttempt.Denied(FireDenial.Inspecting);
                case AmmoInteraction.Throwing: return FireAttempt.Denied(FireDenial.Inspecting);
            }

            if (sprinting)
            {
                return FireAttempt.Denied(FireDenial.Sprinting);
            }

            if (now - LastFireTime + 1e-4f < Weapon.SecondsPerShot)
            {
                return FireAttempt.Denied(FireDenial.RateOfFire);
            }

            if (!RoundChambered || Chambered == null || Chambered.Rounds <= 0)
            {
                RoundChambered = false;
                Emit(AmmoEventKind.DryFireClick, now, Chambered?.DefinitionId, 0);
                return FireAttempt.Denied(FireDenial.WeaponEmpty);
            }

            Chambered.ConsumeOne();
            RoundsFired++;
            LastFireTime = now;
            bool wasLastRound = Chambered.Rounds <= 0;
            if (wasLastRound)
            {
                RoundChambered = false;
                Emit(AmmoEventKind.LastRound, now, Chambered.DefinitionId, 0);
            }

            return FireAttempt.Success(Chambered.Rounds, wasLastRound);
        }

        // ------------------------------------------------------------ R key handling

        /// <summary>R went down. Whether this becomes a reload or an inspection is decided by hold time.</summary>
        public void PressReloadKey(float now)
        {
            if (State != AmmoInteraction.Idle)
            {
                return;
            }

            HoldStartedAt = now;
            State = AmmoInteraction.PendingDecision;
            StateStartedAt = now;
        }

        /// <summary>R came up before the hold threshold: this was a reload request.</summary>
        public void ReleaseReloadKey(float now)
        {
            if (State != AmmoInteraction.PendingDecision)
            {
                if (State == AmmoInteraction.Inspecting)
                {
                    EndInspect(now);
                }

                return;
            }

            float held = now - HoldStartedAt;
            State = AmmoInteraction.Idle;
            if (held < Options.HoldToInspectSeconds)
            {
                BeginReload(now);
                return;
            }

            // The hold threshold was reached.  Normally Update() already switched the
            // state to Inspecting; if the caller never ticked in between, run the whole
            // inspection now so the behaviour is identical either way.
            BeginInspect(now);
            EndInspect(now);
        }

        /// <summary>Advance the state machine.</summary>
        public void Update(float now)
        {
            EmitDueScheduled(now);

            switch (State)
            {
                case AmmoInteraction.PendingDecision:
                    if (now - HoldStartedAt >= Options.HoldToInspectSeconds)
                    {
                        BeginInspect(now);
                    }

                    break;

                case AmmoInteraction.Reloading:
                case AmmoInteraction.Chambering:
                case AmmoInteraction.FumbleRecovery:
                case AmmoInteraction.ReturningFromInspect:
                case AmmoInteraction.Throwing:
                    if (now >= StateEndsAt)
                    {
                        CompleteState(now);
                    }

                    break;
            }
        }

        /// <summary>Called by the match when the operator dies or the round ends.</summary>
        public void CancelInteraction()
        {
            State = AmmoInteraction.Idle;
            CurrentReloadType = ReloadType.None;
            _schedule.Clear();
        }

        // ------------------------------------------------------------ reload

        public bool BeginReload(float now)
        {
            if (State != AmmoInteraction.Idle)
            {
                return false;
            }

            if (Chambered != null && Chambered.IsFull)
            {
                // Nothing to do: the magazine inside is already full.
                return false;
            }

            int index = PickSpareMagazineIndex();
            if (index < 0)
            {
                Emit(AmmoEventKind.OutOfAmmunition, now, Chambered?.DefinitionId, Chambered?.Rounds ?? 0);
                return false;
            }

            MagazineInstance spare = _pouch[index];
            bool dry = Chambered == null || Chambered.Rounds <= 0 || !RoundChambered;
            CurrentReloadType = dry ? ReloadType.Dry : ReloadType.Tactical;

            float baseDuration = dry ? Weapon.ReloadDrySeconds : Weapon.ReloadTacticalSeconds;
            MagazineDefinition spareDefinition = Library?.GetMagazine(spare.DefinitionId);
            float multiplier = spareDefinition?.ReloadTimeMultiplier ?? 1f;
            float duration = Math.Max(0.35f, baseDuration * multiplier);

            bool fumbled = Options.AllowFumbles && spareDefinition != null &&
                           _random.Chance(spareDefinition.FumbleChance);
            if (fumbled)
            {
                duration += 0.9f;
                FumbleCount++;
            }

            State = AmmoInteraction.Reloading;
            StateStartedAt = now;
            StateEndsAt = now + duration;
            ReloadCount++;

            _schedule.Clear();
            string outgoingId = Chambered?.DefinitionId;
            string incomingId = spare.DefinitionId;

            Schedule(now + duration * 0.30f, AmmoEventKind.MagazineOut, outgoingId);
            if (fumbled)
            {
                Schedule(now + duration * 0.46f, AmmoEventKind.FumbleStart, incomingId);
                Schedule(now + duration * 0.46f + 0.9f, AmmoEventKind.FumbleRecovered, incomingId);
            }

            float insertAt = now + duration * (dry ? 0.66f : 0.70f);
            Schedule(insertAt, AmmoEventKind.MagazineIn, incomingId);
            if (dry)
            {
                Schedule(now + duration * 0.9f, AmmoEventKind.BoltRelease, incomingId);
            }

            Schedule(StateEndsAt, AmmoEventKind.ReloadComplete, incomingId);

            // Remember the swap so it is applied when the magazine physically goes in.
            _pendingSwapIndex = index;
            _pendingSwapAt = insertAt;
            _pendingSwapIsDry = dry;
            return true;
        }

        private int _pendingSwapIndex = -1;
        private float _pendingSwapAt;
        private bool _pendingSwapIsDry;

        private void EmitDueScheduled(float now)
        {
            for (int i = 0; i < _schedule.Count; i++)
            {
                ScheduledAmmoEvent item = _schedule[i];
                if (!item.Fired && now >= item.At)
                {
                    item.Fired = true;
                    _schedule[i] = item;
                    if (item.Kind == AmmoEventKind.MagazineIn && State == AmmoInteraction.Reloading)
                    {
                        ApplyMagazineSwap();
                    }

                    Emit(item.Kind, item.At, item.MagazineId, Chambered?.Rounds ?? 0);
                }
            }
        }

        private void ApplyMagazineSwap()
        {
            if (_pendingSwapIndex < 0 || _pendingSwapIndex >= _pouch.Count)
            {
                return;
            }

            MagazineInstance incoming = _pouch[_pendingSwapIndex];
            _pouch.RemoveAt(_pendingSwapIndex);

            MagazineInstance outgoing = Chambered;
            Chambered = incoming;
            RoundChambered = incoming.Rounds > 0;

            if (outgoing != null)
            {
                bool keep = outgoing.Rounds > 0 ? Options.ReturnPartialMagazines : Options.KeepEmptyMagazines;
                if (keep)
                {
                    _pouch.Add(outgoing);
                }
            }

            _pendingSwapIndex = -1;
        }

        private int PickSpareMagazineIndex()
        {
            if (Options.PickPolicy == MagazinePickPolicy.Fullest)
            {
                int best = -1;
                for (int i = 0; i < _pouch.Count; i++)
                {
                    if (_pouch[i].Rounds <= 0)
                    {
                        continue;
                    }

                    if (best < 0 || _pouch[i].Rounds > _pouch[best].Rounds)
                    {
                        best = i;
                    }
                }

                return best;
            }

            for (int i = 0; i < _pouch.Count; i++)
            {
                if (_pouch[i].Rounds > 0)
                {
                    return i;
                }
            }

            return -1;
        }

        // ------------------------------------------------------------ inspection

        public bool BeginInspect(float now)
        {
            if (State == AmmoInteraction.Reloading || State == AmmoInteraction.Chambering ||
                State == AmmoInteraction.FumbleRecovery)
            {
                return false;
            }

            State = AmmoInteraction.Inspecting;
            StateStartedAt = now;
            StateEndsAt = now + Math.Max(0.2f, Weapon.InspectSeconds);
            InspectReadableAt = now + Weapon.InspectSeconds * 0.45f;
            InspectCount++;
            _schedule.Clear();
            Schedule(now, AmmoEventKind.InspectStart, Chambered?.DefinitionId);
            Schedule(InspectReadableAt, AmmoEventKind.InspectReadable, Chambered?.DefinitionId);
            return true;
        }

        public void EndInspect(float now)
        {
            if (State != AmmoInteraction.Inspecting)
            {
                return;
            }

            Emit(AmmoEventKind.InspectEnd, now, Chambered?.DefinitionId, Chambered?.Rounds ?? 0);
            State = AmmoInteraction.ReturningFromInspect;
            StateStartedAt = now;
            StateEndsAt = now + Math.Max(0.2f, Weapon.InspectReturnSeconds);
            _schedule.Clear();
        }

        private void CompleteState(float now)
        {
            switch (State)
            {
                case AmmoInteraction.Reloading:
                    if (_pendingSwapIndex >= 0)
                    {
                        // Safety net: if the swap event was skipped, apply it now.
                        ApplyMagazineSwap();
                    }

                    RoundChambered = Chambered != null && Chambered.Rounds > 0;
                    State = AmmoInteraction.Idle;
                    CurrentReloadType = ReloadType.None;
                    _schedule.Clear();
                    break;

                case AmmoInteraction.ReturningFromInspect:
                    State = AmmoInteraction.Idle;
                    _schedule.Clear();
                    break;

                case AmmoInteraction.Chambering:
                    RoundChambered = Chambered != null && Chambered.Rounds > 0;
                    State = AmmoInteraction.Idle;
                    _schedule.Clear();
                    break;

                case AmmoInteraction.Throwing:
                    State = AmmoInteraction.Idle;
                    _schedule.Clear();
                    break;

                default:
                    State = AmmoInteraction.Idle;
                    break;
            }
        }

        /// <summary>Chamber a round manually after an empty-chamber handling action.</summary>
        public bool BeginChambering(float now)
        {
            if (State != AmmoInteraction.Idle || RoundChambered)
            {
                return false;
            }

            if (Chambered == null || Chambered.Rounds <= 0)
            {
                return false;
            }

            State = AmmoInteraction.Chambering;
            StateStartedAt = now;
            StateEndsAt = now + Math.Max(0.2f, Weapon.ChargeSeconds);
            _schedule.Clear();
            Schedule(StateEndsAt, AmmoEventKind.ChamberRound, Chambered.DefinitionId);
            return true;
        }

        private void Schedule(float at, AmmoEventKind kind, string magazineId)
        {
            _schedule.Add(new ScheduledAmmoEvent(at, kind, magazineId));
        }

        private void Emit(AmmoEventKind kind, float timeSeconds, string magazineId, int rounds)
        {
            PendingEvents.Add(new AmmoEvent(kind, timeSeconds, magazineId, rounds));
        }

        public void DrainEvents(List<AmmoEvent> into)
        {
            for (int i = 0; i < PendingEvents.Count; i++)
            {
                into.Add(PendingEvents[i]);
            }

            PendingEvents.Clear();
        }

        // ------------------------------------------------------------ HUD

        /// <summary>
        /// Build the HUD contract.  The magazine strip is always shown as
        /// white / yellow / red slots; exact numbers appear only during an inspection.
        /// </summary>
        public AmmoHudSnapshot BuildHudSnapshot(float now, int grenadesRemaining = 0)
        {
            _lastUpdate = now;
            AmmoHudSnapshot snapshot = new AmmoHudSnapshot
            {
                IsInspecting = MayShowExactCount,
                WeaponIsEmpty = !RoundChambered,
                HasReserveAmmunition = HasReserveAmmunition,
                Interaction = State,
                Reload = CurrentReloadType,
                WeaponId = Weapon?.Id ?? string.Empty,
                GrenadesRemaining = grenadesRemaining
            };

            if (State == AmmoInteraction.Reloading && StateEndsAt > StateStartedAt)
            {
                snapshot.ReloadProgress01 = MathUtil.Clamp01(
                    (now - StateStartedAt) / (StateEndsAt - StateStartedAt));
            }

            bool readable = MayShowExactCount;
            if (Chambered != null)
            {
                MagazineHudSlot slot = new MagazineHudSlot(Chambered.DefinitionId, Chambered.UiState, true)
                {
                    Capacity = Chambered.Capacity
                };
                if (readable)
                {
                    slot.ShowsExactCount = true;
                    slot.ExactRounds = Chambered.Rounds;
                    snapshot.InspectedRounds = Chambered.Rounds;
                    snapshot.InspectedCapacity = Chambered.Capacity;
                    snapshot.InspectedMagazineName = InspectedMagazineName;
                }

                snapshot.Slots.Add(slot);
            }

            foreach (MagazineInstance magazine in _pouch)
            {
                snapshot.Slots.Add(new MagazineHudSlot(magazine.DefinitionId, magazine.UiState, false)
                {
                    Capacity = magazine.Capacity
                });
            }

            return snapshot;
        }

        private float _lastUpdate;

        /// <summary>Server time of the last tick, used by HUD progress bars.</summary>
        public float LastUpdateTime => _lastUpdate;

        /// <summary>Advance the state machine and remember the authoritative time.</summary>
        public void Tick(float now)
        {
            _lastUpdate = now;
            Update(now);
        }
    }
}
