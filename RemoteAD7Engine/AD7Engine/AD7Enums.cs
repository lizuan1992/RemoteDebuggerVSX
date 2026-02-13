using System;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Debugger.Interop;

namespace RemoteAD7Engine
{
    internal class AD7Enum<TElement, TEnumInterface>
        where TEnumInterface : class
    {
        private readonly object _syncRoot = new object();
        private readonly TElement[] _data;
        private uint _position;

        public AD7Enum(TElement[] data)
        {
            _data = data ?? Array.Empty<TElement>();
            _position = 0;
        }

        public int Clone(out TEnumInterface ppEnum)
        {
            try
            {
                var clone = (AD7Enum<TElement, TEnumInterface>)Activator.CreateInstance(GetType(), new object[] { _data });
                clone._position = _position;

                ppEnum = clone as TEnumInterface;
                return ppEnum != null ? VSConstants.S_OK : VSConstants.E_FAIL;
            }
            catch
            {
                ppEnum = null;
                return VSConstants.E_FAIL;
            }
        }

        public int GetCount(out uint pcelt)
        {
            pcelt = (uint)_data.Length;
            return VSConstants.S_OK;
        }

        public int Next(uint celt, TElement[] rgelt, out uint celtFetched)
        {
            return Move(celt, rgelt, out celtFetched);
        }

        public int Reset()
        {
            lock (_syncRoot)
            {
                _position = 0;
                return VSConstants.S_OK;
            }
        }

        public int Skip(uint celt)
        {
            return Move(celt, null, out _);
        }

        private int Move(uint celt, TElement[] rgelt, out uint celtFetched)
        {
            lock (_syncRoot)
            {
                var hr = VSConstants.S_OK;
                celtFetched = (uint)_data.Length - _position;

                if (celt > celtFetched)
                {
                    hr = VSConstants.S_FALSE;
                }
                else if (celt < celtFetched)
                {
                    celtFetched = celt;
                }

                if (rgelt != null)
                {
                    for (var i = 0; i < celtFetched; i++)
                    {
                        rgelt[i] = _data[_position + i];
                    }
                }

                _position += celtFetched;
                return hr;
            }
        }
    }

    internal sealed class AD7PropertyInfoEnum : AD7Enum<DEBUG_PROPERTY_INFO, IEnumDebugPropertyInfo2>, IEnumDebugPropertyInfo2
    {
        public AD7PropertyInfoEnum(DEBUG_PROPERTY_INFO[] data)
            : base(data)
        {
        }
    }

    internal sealed class AD7ThreadEnum : AD7Enum<IDebugThread2, IEnumDebugThreads2>, IEnumDebugThreads2
    {
        public AD7ThreadEnum(IDebugThread2[] data)
            : base(data)
        {
        }

        int IEnumDebugThreads2.Next(uint celt, IDebugThread2[] rgelt, ref uint pceltFetched)
        {
            return Next(celt, rgelt, out pceltFetched);
        }
    }

    internal sealed class AD7FrameInfoEnum : AD7Enum<FRAMEINFO, IEnumDebugFrameInfo2>, IEnumDebugFrameInfo2
    {
        public AD7FrameInfoEnum(FRAMEINFO[] data)
            : base(data)
        {
        }

        int IEnumDebugFrameInfo2.Next(uint celt, FRAMEINFO[] rgelt, ref uint pceltFetched)
        {
            return Next(celt, rgelt, out pceltFetched);
        }
    }

    internal sealed class AD7ErrorBreakpointsEnum : AD7Enum<IDebugErrorBreakpoint2, IEnumDebugErrorBreakpoints2>, IEnumDebugErrorBreakpoints2
    {
        public AD7ErrorBreakpointsEnum(IDebugErrorBreakpoint2[] breakpoints)
            : base(breakpoints)
        {
        }

        int IEnumDebugErrorBreakpoints2.Next(uint celt, IDebugErrorBreakpoint2[] rgelt, ref uint pceltFetched)
        {
            return Next(celt, rgelt, out pceltFetched);
        }
    }

    internal sealed class AD7BoundBreakpointsEnum : AD7Enum<IDebugBoundBreakpoint2, IEnumDebugBoundBreakpoints2>, IEnumDebugBoundBreakpoints2
    {
        public AD7BoundBreakpointsEnum(IDebugBoundBreakpoint2[] breakpoints)
            : base(breakpoints)
        {
        }

        int IEnumDebugBoundBreakpoints2.Next(uint celt, IDebugBoundBreakpoint2[] rgelt, ref uint pceltFetched)
        {
            return Next(celt, rgelt, out pceltFetched);
        }
    }

    internal sealed class AD7PortEnum : AD7Enum<IDebugPort2, IEnumDebugPorts2>, IEnumDebugPorts2
    {
        public AD7PortEnum(IDebugPort2[] data)
            : base(data)
        {
        }

        int IEnumDebugPorts2.Next(uint celt, IDebugPort2[] rgelt, ref uint pceltFetched)
        {
            return Next(celt, rgelt, out pceltFetched);
        }
    }

    internal sealed class AD7ProcessEnum : AD7Enum<IDebugProcess2, IEnumDebugProcesses2>, IEnumDebugProcesses2
    {
        public AD7ProcessEnum(IDebugProcess2[] data)
            : base(data)
        {
        }

        int IEnumDebugProcesses2.Next(uint celt, IDebugProcess2[] rgelt, ref uint pceltFetched)
        {
            return Next(celt, rgelt, out pceltFetched);
        }
    }

    internal sealed class AD7ModuleEnum : AD7Enum<IDebugModule2, IEnumDebugModules2>, IEnumDebugModules2
    {
        public AD7ModuleEnum(IDebugModule2[] data)
            : base(data)
        {
        }

        int IEnumDebugModules2.Next(uint celt, IDebugModule2[] rgelt, ref uint pceltFetched)
        {
            return Next(celt, rgelt, out pceltFetched);
        }
    }
}
