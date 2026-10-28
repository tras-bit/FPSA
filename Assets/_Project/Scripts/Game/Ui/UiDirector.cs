using System.Collections.Generic;
using UnityEngine;

namespace Kordon.Game.Ui
{
    /// <summary>A screen the director can show: built once, shown and hidden by name.</summary>
    public interface IUiScreen
    {
        string Name { get; }
        RectTransform Root { get; }
        void OnShow();
        void OnHide();
    }

    /// <summary>
    /// Owns the screen stack and the mouse: menus unlock and show the cursor, the
    /// match locks and hides it.  One place, so no screen can forget to hand the
    /// pointer back.
    /// </summary>
    public sealed class UiDirector : MonoBehaviour
    {
        private readonly Dictionary<string, IUiScreen> _screens =
            new Dictionary<string, IUiScreen>();
        private readonly Stack<IUiScreen> _stack = new Stack<IUiScreen>();

        public bool InMatch { get; private set; }

        public void Register(IUiScreen screen)
        {
            _screens[screen.Name] = screen;
            screen.Root.gameObject.SetActive(false);
        }

        public T Get<T>(string name) where T : class, IUiScreen =>
            _screens.TryGetValue(name, out IUiScreen screen) ? screen as T : null;

        public void Push(string name)
        {
            if (!_screens.TryGetValue(name, out IUiScreen screen))
            {
                return;
            }

            _stack.Push(screen);
            screen.Root.gameObject.SetActive(true);
            screen.OnShow();
            Cursor.visible = true;
            Cursor.lockState = CursorLockMode.None;
        }

        public void Pop()
        {
            if (_stack.Count == 0)
            {
                return;
            }

            IUiScreen screen = _stack.Pop();
            screen.Root.gameObject.SetActive(false);
            screen.OnHide();
            SyncCursor();
        }

        public void Clear()
        {
            while (_stack.Count > 0)
            {
                IUiScreen screen = _stack.Pop();
                screen.Root.gameObject.SetActive(false);
                screen.OnHide();
            }

            SyncCursor();
        }

        public void EnterMatch()
        {
            Clear();
            InMatch = true;
            SyncCursor();
        }

        public void LeaveMatch()
        {
            InMatch = false;
            SyncCursor();
        }

        private void SyncCursor()
        {
            bool menuOpen = _stack.Count > 0;
            Cursor.visible = menuOpen || !InMatch;
            Cursor.lockState = menuOpen || !InMatch ? CursorLockMode.None : CursorLockMode.Locked;
        }
    }
}
