using System;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Debugger.Interop;

namespace RemoteAD7Engine
{
    internal sealed class AD7DocumentContext : IDebugDocumentContext2, IDebugCodeContext2
    {
        private static readonly Guid CppLanguageGuid = new Guid("3A12D0B7-1E22-11d3-B34E-00C04F68CD7C");

        private readonly int _lineOffset;
        private readonly string _file;
        private readonly int _baseLine;

        internal AD7DocumentContext(string file, int line, int lineOffset = 0)
        {
            _file = file;
            _baseLine = line;
            _lineOffset = lineOffset;
        }

        private string GetCurrFile() => _file ?? "unknown";

        private int GetCurrLine() => _baseLine + _lineOffset;

        #region IDebugDocumentContext2 Members

        int IDebugDocumentContext2.Compare(
            enum_DOCCONTEXT_COMPARE Compare,
            IDebugDocumentContext2[] rgpDocContextSet,
            uint dwDocContextSetLen,
            out uint pdwDocContext)
        {
            pdwDocContext = 0;

            try
            {
                if (rgpDocContextSet == null || dwDocContextSetLen == 0)
                {
                    return VSConstants.E_INVALIDARG;
                }

                for (uint i = 0; i < dwDocContextSetLen; i++)
                {
                    var other = rgpDocContextSet[i] as AD7DocumentContext;
                    if (other == null)
                    {
                        continue;
                    }

                    var sameFile = string.Equals(
                        GetCurrFile(),
                        other.GetCurrFile(),
                        StringComparison.OrdinalIgnoreCase);

                    var sameLine = GetCurrLine() == other.GetCurrLine();

                    if (sameFile && sameLine)
                    {
                        pdwDocContext = i;
                        return VSConstants.S_OK;
                    }
                }

                return VSConstants.S_FALSE;
            }
            catch
            {
                pdwDocContext = 0;
                return VSConstants.E_FAIL;
            }
        }

        int IDebugDocumentContext2.EnumCodeContexts(out IEnumDebugCodeContexts2 ppEnumCodeCxts)
        {
            ppEnumCodeCxts = new AD7CodeContextEnum(new IDebugCodeContext2[] { this });
            return VSConstants.S_OK;
        }

        int IDebugDocumentContext2.GetDocument(out IDebugDocument2 ppDocument)
        {
            ppDocument = null;
            return VSConstants.E_NOTIMPL;
        }

        public int GetLanguageInfo(ref string pbstrLanguage, ref Guid pguidLanguage)
        {
            pbstrLanguage = "C++";
            pguidLanguage = CppLanguageGuid;
            return VSConstants.S_OK;
        }

        int IDebugDocumentContext2.GetName(enum_GETNAME_TYPE gnType, out string pbstrFileName)
        {
            pbstrFileName = GetCurrFile();
            return VSConstants.S_OK;
        }

        int IDebugDocumentContext2.GetSourceRange(TEXT_POSITION[] pBegPosition, TEXT_POSITION[] pEndPosition)
        {
            if (pBegPosition == null || pBegPosition.Length == 0 || pEndPosition == null || pEndPosition.Length == 0)
            {
                return VSConstants.E_INVALIDARG;
            }

            var currLine = GetCurrLine();
            pBegPosition[0].dwColumn = 0;
            pBegPosition[0].dwLine = (uint)currLine;
            pEndPosition[0].dwColumn = 0;
            pEndPosition[0].dwLine = (uint)currLine;

            return VSConstants.S_OK;
        }

        int IDebugDocumentContext2.GetStatementRange(TEXT_POSITION[] pBegPosition, TEXT_POSITION[] pEndPosition)
        {
            if (pBegPosition == null || pBegPosition.Length == 0 || pEndPosition == null || pEndPosition.Length == 0)
            {
                return VSConstants.E_INVALIDARG;
            }

            var currLine = GetCurrLine();
            pBegPosition[0].dwColumn = 0;
            pBegPosition[0].dwLine = (uint)currLine;
            pEndPosition[0].dwColumn = 0;
            pEndPosition[0].dwLine = (uint)currLine;

            return VSConstants.S_OK;
        }

        int IDebugDocumentContext2.Seek(int nCount, out IDebugDocumentContext2 ppDocContext)
        {
            var newOffset = _lineOffset + nCount;
            ppDocContext = new AD7DocumentContext(_file, _baseLine, newOffset);
            return VSConstants.S_OK;
        }

        #endregion

        public int Add(ulong dwCount, out IDebugMemoryContext2 ppMemCxt)
        {
            var delta = (uint)Math.Min(uint.MaxValue, dwCount);
            var newOffset = _lineOffset + (int)delta;
            ppMemCxt = new AD7DocumentContext(_file, _baseLine, newOffset);
            return VSConstants.S_OK;
        }

        public int GetDocumentContext(out IDebugDocumentContext2 ppSrcCxt)
        {
            ppSrcCxt = this;
            return VSConstants.S_OK;
        }

        public int GetInfo(enum_CONTEXT_INFO_FIELDS dwFields, CONTEXT_INFO[] pinfo)
        {
            if (pinfo == null || pinfo.Length == 0)
            {
                return VSConstants.E_INVALIDARG;
            }

            pinfo[0].dwFields = 0;

            if ((dwFields & enum_CONTEXT_INFO_FIELDS.CIF_MODULEURL) != 0 && !string.IsNullOrEmpty(GetCurrFile()))
            {
                pinfo[0].bstrModuleUrl = GetCurrFile();
                pinfo[0].dwFields |= enum_CONTEXT_INFO_FIELDS.CIF_MODULEURL;
            }

            if ((dwFields & enum_CONTEXT_INFO_FIELDS.CIF_ADDRESS) != 0)
            {
                pinfo[0].bstrAddress = string.Concat(GetCurrFile() ?? string.Empty, ":", GetCurrLine().ToString());
                pinfo[0].dwFields |= enum_CONTEXT_INFO_FIELDS.CIF_ADDRESS;
            }

            return VSConstants.S_OK;
        }

        public int Subtract(ulong dwCount, out IDebugMemoryContext2 ppMemCxt)
        {
            var delta = (uint)Math.Min(uint.MaxValue, dwCount);
            var newOffset = _lineOffset - (int)delta;
            if (newOffset < 0) newOffset = 0;
            ppMemCxt = new AD7DocumentContext(_file, _baseLine, newOffset);
            return VSConstants.S_OK;
        }

        public int Compare(
            enum_CONTEXT_COMPARE Compare,
            IDebugMemoryContext2[] rgpMemoryContextSet,
            uint dwMemoryContextSetLen,
            out uint pdwMemoryContext)
        {
            pdwMemoryContext = 0;

            try
            {
                if (rgpMemoryContextSet == null || dwMemoryContextSetLen == 0)
                {
                    return VSConstants.E_INVALIDARG;
                }

                for (uint i = 0; i < dwMemoryContextSetLen; i++)
                {
                    var other = rgpMemoryContextSet[i] as AD7DocumentContext;
                    if (other == null)
                    {
                        continue;
                    }

                    if (string.Equals(GetCurrFile() ?? string.Empty, other.GetCurrFile() ?? string.Empty, StringComparison.OrdinalIgnoreCase)
                        && GetCurrLine() == other.GetCurrLine())
                    {
                        pdwMemoryContext = i;
                        return VSConstants.S_OK;
                    }
                }

                return VSConstants.S_FALSE;
            }
            catch
            {
                pdwMemoryContext = 0;
                return VSConstants.E_FAIL;
            }
        }

        public int GetName(out string pbstrName)
        {
            pbstrName = GetCurrFile();
            return VSConstants.S_OK;
        }
    }
}
