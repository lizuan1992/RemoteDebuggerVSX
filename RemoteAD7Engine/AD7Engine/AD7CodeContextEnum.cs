using Microsoft.VisualStudio.Debugger.Interop;

namespace RemoteAD7Engine
{
    internal sealed class AD7CodeContextEnum : AD7Enum<IDebugCodeContext2, IEnumDebugCodeContexts2>, IEnumDebugCodeContexts2
    {
        public AD7CodeContextEnum(IDebugCodeContext2[] data)
            : base(data)
        {
        }

        int IEnumDebugCodeContexts2.Next(uint celt, IDebugCodeContext2[] rgelt, ref uint pceltFetched)
        {
            return Next(celt, rgelt, out pceltFetched);
        }
    }
}
