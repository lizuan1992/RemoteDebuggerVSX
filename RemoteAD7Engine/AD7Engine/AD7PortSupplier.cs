using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Debugger.Interop;

namespace RemoteAD7Engine
{
    [ComVisible(true)]
    [Guid(PortSupplierGuidString)]
    public class AD7PortSupplier : IDebugPortSupplier2
    {
        private const string PortSupplierGuidString = "AD701326-0C6D-4334-BA0D-E94D0F91D440";

        private readonly object _syncRoot = new object();
        private readonly List<IDebugPort2> _ports = new List<IDebugPort2>();

        public int AddPort(IDebugPortRequest2 pRequest, out IDebugPort2 ppPort)
        {
            ppPort = new AD7Port(this, pRequest);

            lock (_syncRoot)
            {
                _ports.Add(ppPort);
            }

            return VSConstants.S_OK;
        }

        public int CanAddPort()
        {
            return VSConstants.S_OK;
        }

        public int EnumPorts(out IEnumDebugPorts2 ppEnum)
        {
            IDebugPort2[] ports;
            lock (_syncRoot)
            {
                ports = _ports.ToArray();
            }

            ppEnum = new AD7PortEnum(ports);
            return VSConstants.S_OK;
        }

        public int GetPort(ref Guid guidPort, out IDebugPort2 ppPort)
        {
            ppPort = null;
            return VSConstants.E_NOTIMPL;
        }

        public int GetPortSupplierId(out Guid pguidPortSupplier)
        {
            pguidPortSupplier = new Guid(PortSupplierGuidString);
            return VSConstants.S_OK;
        }

        public int GetPortSupplierName(out string pbstrName)
        {
            pbstrName = "Remote Port Supplier";
            return VSConstants.S_OK;
        }

        public int RemovePort(IDebugPort2 pPort)
        {
            return VSConstants.E_NOTIMPL;
        }
    }
}
