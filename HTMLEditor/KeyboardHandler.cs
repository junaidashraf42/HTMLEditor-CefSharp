using System;
using System.Windows.Forms;
using CefSharp;

namespace HTMLEditor
{
    public class KeyboardHandler : IKeyboardHandler
    {
        private readonly Form1 _form;

        public KeyboardHandler(Form1 form)
        {
            _form = form;
        }

        public bool OnKeyEvent(IWebBrowser chromiumWebBrowser, IBrowser browser, KeyType type, int windowsKeyCode, int nativeKeyCode, CefEventFlags modifiers, bool isSystemKey)
        {
            // Handle Ctrl+F
            if (type == KeyType.KeyUp && windowsKeyCode == (int)Keys.F && modifiers.HasFlag(CefEventFlags.ControlDown))
            {
                // Invoke on UI thread to show find dialog
                _form.BeginInvoke((Action)(() =>
                {
                    _form.ShowFindDialog();
                }));
                return true; // Handled
            }

            // Handle Escape to close find dialog
            if (type == KeyType.KeyUp && windowsKeyCode == (int)Keys.Escape)
            {
                _form.BeginInvoke((Action)(() =>
                {
                    _form.HideFindDialogIfVisible();
                }));
                return true; // Handled
            }

            return false; // Not handled, let browser process
        }

        public bool OnPreKeyEvent(IWebBrowser chromiumWebBrowser, IBrowser browser, KeyType type, int windowsKeyCode, int nativeKeyCode, CefEventFlags modifiers, bool isSystemKey, ref bool isKeyboardShortcut)
        {
            // Pre-handle Ctrl+F to prevent browser's default find behavior
            if (type == KeyType.KeyDown && windowsKeyCode == (int)Keys.F && modifiers.HasFlag(CefEventFlags.ControlDown))
            {
                isKeyboardShortcut = false;
                return true; // Prevent browser's default Ctrl+F
            }

            return false;
        }
    }
}
