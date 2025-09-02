using System;
using CefSharp;
using CefSharp.Structs;

namespace HTMLEditor
{
    public class FindHandler : IFindHandler
    {
        private readonly Form1 _form;

        public FindHandler(Form1 form)
        {
            _form = form;
        }

        public void OnFindResult(IWebBrowser chromiumWebBrowser, IBrowser browser, int identifier, int count, Rect selectionRect, int activeMatchOrdinal, bool finalUpdate)
        {
            // This method is called on the CEF UI thread, so we need to invoke on the form's thread
            _form.BeginInvoke((Action)(() =>
            {
                _form.UpdateFindResults(identifier, count, activeMatchOrdinal, finalUpdate);
            }));
        }
    }
}
