using System;
using System.Collections.Generic;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Debugger.Interop;

namespace RemoteAD7Engine
{
    internal sealed class AD7Property : IDebugProperty2, IDebugProperty3
    {
        private AD7Engine _engine;
        private AD7Thread _thread;
        private AD7StackFrame _frame;
        private string _expression;

        public AD7Property Parent { get; set; }
        public long Addr { get; set; } // Variable address

        public AD7Property(long addr, AD7Engine engine, AD7Thread thread, AD7StackFrame frame, string expression = null)
        {
            Addr = addr;
            _engine = engine;
            _thread = thread;
            _frame = frame;
            _expression = expression;
        }

        private ProtocolVariable GetVariable()
        {
            return RemoteState.MemoryVariables.TryGetValue(Addr, out var pv) ? pv : null;
        }

        /// <summary>
        /// Recursively enumerates child properties, including protocol array elements.
        /// </summary>
        public int EnumChildren(
            enum_DEBUGPROP_INFO_FLAGS dwFields,
            uint dwRadix,
            ref Guid guidFilter,
            enum_DBG_ATTRIB_FLAGS dwAttribFilter,
            string pszNameFilter,
            uint dwTimeout,
            out IEnumDebugPropertyInfo2 ppEnum)
        {
            var props = new List<DEBUG_PROPERTY_INFO>();
            var pv = GetVariable();

            if (pv == null)
            {
                ppEnum = null;
                return VSConstants.S_FALSE;
            }

            // Check if there are elements
            bool hasElements = pv.Elements != null && pv.Elements.Count > 0;

            // Lazy fetch children if not already loaded and this is an expandable object or array
            bool needLoad = false;
            if (pv.Elements == null || pv.Elements.Count == 0)
            {
                needLoad = true;
            }
            else if (pv.Size > 0 && pv.Elements.Count < pv.Size)
            {
                needLoad = true;
            }

            if (needLoad && _engine != null && _thread != null && _frame != null)
            {
                int start = 0;
                int count = 0;
                if (pv.Elements != null && pv.Elements.Count > 0)
                {
                    start = pv.Elements.Count;
                    count = pv.Size - pv.Elements.Count;
                }
                else
                {
                    start = 0;
                    count = pv.Size > 0 ? pv.Size : 0; // Default to size if known, else 0
                }

                _engine.SendDebugCommand("get_property", new Dictionary<string, object>
                {
                    { "threadId", _thread.ThreadID },
                    { "frameId", _frame.FrameId },
                    { "addr", Addr },
                    { "typeId", pv.TypeId },
                    { "start", start },
                    { "count", count }
                }, true);

                // Re-fetch after update
                pv = GetVariable();
                hasElements = pv?.Elements != null && pv.Elements.Count > 0;
            }

            if (!hasElements)
            {
                ppEnum = null;
                return VSConstants.S_FALSE;
            }

            // Enumerate child elements
            if (pv.Elements != null)
            {
                foreach (var childAddr in pv.Elements)
                {
                    if (RemoteState.MemoryVariables.TryGetValue(childAddr, out var childPv))
                    {
                        var childProp = new AD7Property(childAddr, _engine, _thread, _frame) { Parent = this };
                        var dpi = new DEBUG_PROPERTY_INFO[1];
                        ((IDebugProperty2)childProp).GetPropertyInfo(dwFields, dwRadix, dwTimeout, null, 0, dpi);
                        props.Add(dpi[0]);
                    }
                }
            }

            if (props.Count > 0)
            {
                ppEnum = new AD7PropertyInfoEnum(props.ToArray());
                return VSConstants.S_OK;
            }

            ppEnum = null;
            return VSConstants.S_FALSE;
        }

        public int GetDerivedMostProperty(out IDebugProperty2 ppDerivedMost)
        {
            ppDerivedMost = null;
            return VSConstants.E_NOTIMPL;
        }

        public int GetExtendedInfo(ref Guid guidExtendedInfo, out object pExtendedInfo)
        {
            pExtendedInfo = null;
            return VSConstants.E_NOTIMPL;
        }

        public int GetMemoryBytes(out IDebugMemoryBytes2 ppMemoryBytes)
        {
            ppMemoryBytes = null;
            return VSConstants.E_NOTIMPL;
        }

        public int GetMemoryContext(out IDebugMemoryContext2 ppMemory)
        {
            ppMemory = null;
            return VSConstants.E_NOTIMPL;
        }

        public int GetReference(out IDebugReference2 ppReference)
        {
            ppReference = null;
            return VSConstants.E_NOTIMPL;
        }

        public int GetSize(out uint pdwSize)
        {
            pdwSize = 0;
            return VSConstants.E_NOTIMPL;
        }

        public int SetValueAsReference(IDebugReference2[] rgpArgs, uint dwArgCount, IDebugReference2 pValue, uint dwTimeout)
        {
            return VSConstants.E_NOTIMPL;
        }

        public int GetParent(out IDebugProperty2 ppParent)
        {
            ppParent = Parent;
            return VSConstants.S_OK;
        }

        public int GetPropertyInfo(
            enum_DEBUGPROP_INFO_FLAGS dwFields,
            uint dwRadix,
            uint dwTimeout,
            IDebugReference2[] rgpArgs,
            uint dwArgCount,
            DEBUG_PROPERTY_INFO[] pPropertyInfo)
        {
            var info = new DEBUG_PROPERTY_INFO
            {
                dwFields = 0
            };

            var pv = GetVariable();
            if (pv == null)
            {
                pPropertyInfo[0] = info;
                return VSConstants.S_OK;
            }

            if ((dwFields & enum_DEBUGPROP_INFO_FLAGS.DEBUGPROP_INFO_FULLNAME) != 0)
            {
                info.bstrFullName = _expression ?? pv.Name;
                info.dwFields |= enum_DEBUGPROP_INFO_FLAGS.DEBUGPROP_INFO_FULLNAME;
            }

            if ((dwFields & enum_DEBUGPROP_INFO_FLAGS.DEBUGPROP_INFO_NAME) != 0)
            {
                info.bstrName = _expression ?? pv.Name;
                info.dwFields |= enum_DEBUGPROP_INFO_FLAGS.DEBUGPROP_INFO_NAME;
            }

            if ((dwFields & enum_DEBUGPROP_INFO_FLAGS.DEBUGPROP_INFO_VALUE) != 0)
            {
                info.bstrValue = pv.Value;
                info.dwFields |= enum_DEBUGPROP_INFO_FLAGS.DEBUGPROP_INFO_VALUE;
            }

            if ((dwFields & enum_DEBUGPROP_INFO_FLAGS.DEBUGPROP_INFO_TYPE) != 0)
            {
                info.bstrType = pv.Type;
                info.dwFields |= enum_DEBUGPROP_INFO_FLAGS.DEBUGPROP_INFO_TYPE;
            }

            if ((dwFields & enum_DEBUGPROP_INFO_FLAGS.DEBUGPROP_INFO_ATTRIB) != 0)
            {
                if (pv.Size != 0 || (pv.Elements != null && pv.Elements.Count > 0))
                {
                    info.dwAttrib |= enum_DBG_ATTRIB_FLAGS.DBG_ATTRIB_OBJ_IS_EXPANDABLE;
                }

                if (!CanEditValue(pv))
                {
                    info.dwAttrib |= enum_DBG_ATTRIB_FLAGS.DBG_ATTRIB_VALUE_READONLY;
                }

                if (IsStringType(pv))
                {
                    info.dwAttrib |= enum_DBG_ATTRIB_FLAGS.DBG_ATTRIB_VALUE_CUSTOM_VIEWER;
                }

                info.dwFields |= enum_DEBUGPROP_INFO_FLAGS.DEBUGPROP_INFO_ATTRIB;
            }

            if ((dwFields & enum_DEBUGPROP_INFO_FLAGS.DEBUGPROP_INFO_PROP) != 0)
            {
                info.pProperty = this;
                info.dwFields |= enum_DEBUGPROP_INFO_FLAGS.DEBUGPROP_INFO_PROP;
            }

            pPropertyInfo[0] = info;
            return VSConstants.S_OK;
        }

        public int SetValueAsString(string pszValue, uint dwRadix, uint dwTimeout)
        {
            var pv = GetVariable();
            if (pv == null || !CanEditValue(pv))
                return VSConstants.E_NOTIMPL;

            var threadId = _thread.ThreadID;
            var frameId = _frame.FrameId;
            var varAddr = Addr;
            var varTypeId = pv.TypeId;
            var value = pszValue;

            _engine.SendDebugCommand("set_variable", new Dictionary<string, object>
            {
                { "threadId", threadId },
                { "frameId", frameId },
                { "addr", varAddr },
                { "typeId", varTypeId },
                { "value", value }
            }, true);

            if (_engine.State.SyncManager.OperationResult)
            {
                // Update the runtime data with the new value
                if (RemoteState.MemoryVariables.TryGetValue(varAddr, out var variable))
                {
                    variable.Value = pszValue;
                }
                return VSConstants.S_OK;
            }

            return VSConstants.E_FAIL;
        }

        private bool CanEditValue(ProtocolVariable pv)
        {
            if (_engine == null || _thread == null || _frame == null || pv == null)
                return false;

            if (Addr == 0 || pv.TypeId == 0)
                return false;

            // Only allow editing simple/non-expandable values
            if (pv.Size != 0)
                return false;

            if (pv.Elements != null && pv.Elements.Count > 0)
                return false;

            return true;
        }

        private bool IsStringType(ProtocolVariable pv)
        {
            // return pv.Type == "string" || pv.Type == "System.String";
            return pv.Type.IndexOf("string", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        // IDebugProperty3 implementation
        public int GetCustomViewerCount(out uint pcelt)
        {
            pcelt = IsStringType(GetVariable()) ? 2u : 0u;
            return VSConstants.S_OK;
        }

        public int GetCustomViewerList(uint celtSkip, uint celtRequested, DEBUG_CUSTOM_VIEWER[] rgViewers, out uint pceltFetched)
        {
            pceltFetched = 0;
            if (!IsStringType(GetVariable()))
            {
                return VSConstants.S_FALSE;
            }

            var viewers = new[]
            {
                new DEBUG_CUSTOM_VIEWER
                {
                    bstrMenuName = "Auto Type",
                    bstrDescription = "Automatically detect and display string content type",
                    guidLang = new Guid("3A12D0B7-C26C-11D0-B442-00A0244A1DD2"),
                    guidVendor = new Guid("2fcd7913-7a65-4692-8a13-232171ed85df"),
                    bstrMetric = "AD7AutoTypeViewer"
                },
                new DEBUG_CUSTOM_VIEWER
                {
                    bstrMenuName = "Text Type",
                    bstrDescription = "Display string as text",
                    guidLang = new Guid("3A12D0B7-C26C-11D0-B442-00A0244A1DD2"),
                    guidVendor = new Guid("2fcd7913-7a65-4692-8a13-232171ed85df"),
                    bstrMetric = "AD7TextTypeViewer"
                }
            };

            uint available = (uint)viewers.Length;
            if (celtSkip >= available)
            {
                return VSConstants.S_FALSE;
            }

            uint toFetch = Math.Min(celtRequested, available - celtSkip);
            for (uint i = 0; i < toFetch; i++)
            {
                rgViewers[i] = viewers[celtSkip + i];
            }

            pceltFetched = toFetch;
            return VSConstants.S_OK;
        }

        public int GetManagedView(out IDebugManagedObject ppObject)
        {
            ppObject = null;
            return VSConstants.E_NOTIMPL;
        }

        public int GetStringCharLength(out uint pLen)
        {
            pLen = 0;
            return VSConstants.E_NOTIMPL;
        }

        public int GetStringChars(uint buflen, ushort[] rgString, out uint pceltFetched)
        {
            pceltFetched = 0;
            return VSConstants.E_NOTIMPL;
        }

        public int CreateObjectID()
        {
            return VSConstants.E_NOTIMPL;
        }

        public int DestroyObjectID()
        {
            return VSConstants.E_NOTIMPL;
        }

        public int SetValueAsStringWithError(string pszValue, uint dwRadix, uint dwTimeout, out string errorString)
        {
            errorString = null;
            return SetValueAsString(pszValue, dwRadix, dwTimeout);
        }
    }
}
