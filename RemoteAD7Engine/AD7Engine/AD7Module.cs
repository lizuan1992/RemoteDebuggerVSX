using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Debugger.Interop;

namespace RemoteAD7Engine
{
    internal sealed class AD7Module : IDebugModule2
    {
        private readonly string _moduleName;
        private readonly string _path;

        public string ModuleName
        {
            get { return _moduleName; }
        }

        public string ModulePath
        {
            get { return _path; }
        }

        public AD7Module(string moduleName, string path = null)
        {
            _moduleName = moduleName;
            _path = path;
        }

        public int GetInfo(enum_MODULE_INFO_FIELDS dwFields, MODULE_INFO[] pinfo)
        {
            pinfo[0].dwValidFields = enum_MODULE_INFO_FIELDS.MIF_NAME;
            pinfo[0].m_bstrName = _moduleName;

            if ((dwFields & enum_MODULE_INFO_FIELDS.MIF_URL) != 0 && !string.IsNullOrEmpty(_path))
            {
                pinfo[0].dwValidFields |= enum_MODULE_INFO_FIELDS.MIF_URL;
                pinfo[0].m_bstrUrl = _path;
            }

            return VSConstants.S_OK;
        }

        public int ReloadSymbols_Deprecated(string pszUrlToSymbols, out string pbstrDebugMessage)
        {
            pbstrDebugMessage = "Remote debug engine does not support symbol reload.";
            return VSConstants.S_FALSE;
        }
    }
}
