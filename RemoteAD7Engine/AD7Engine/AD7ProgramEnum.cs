using Microsoft.VisualStudio.Debugger.Interop;

namespace RemoteAD7Engine
{
    internal sealed class AD7ProgramEnum : AD7Enum<IDebugProgram2, IEnumDebugPrograms2>, IEnumDebugPrograms2
    {
        public AD7ProgramEnum(IDebugProgram2[] data)
            : base(data)
        {
        }

        int IEnumDebugPrograms2.Next(uint celt, IDebugProgram2[] rgelt, ref uint pceltFetched)
        {
            return Next(celt, rgelt, out pceltFetched);
        }
    }
}
