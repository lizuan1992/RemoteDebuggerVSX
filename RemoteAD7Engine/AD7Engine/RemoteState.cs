using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Collections.Concurrent;

namespace RemoteAD7Engine
{
    internal sealed class FrameInfo
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string File { get; set; }
        public int Line { get; set; }
        public List<long> VariablesAddr { get; } = new List<long>();
    }

    internal sealed class ModuleInfo
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Path { get; set; }
    }

    internal sealed class ThreadInfo
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public Dictionary<int, FrameInfo> Frames { get; } = new Dictionary<int, FrameInfo>();
        public AD7Thread Ad7Thread { get; set; }
        public bool IsStopped { get; set; } = false;
        public string StopReason { get; set; }
        public string CurrFile { get; set; }
        public int CurrLine { get; set; }
    }

    internal sealed class SyncRequestManager
    {
        private readonly ManualResetEventSlim _event = new ManualResetEventSlim(false);
        private bool _lastOperationSuccess;
        private readonly AD7Engine _engine;
        private string _lastCommand;

        public SyncRequestManager(AD7Engine engine)
        {
            _engine = engine;
        }

        public void Set(string command)
        {
            if (command == null)
                _event.Set();
            else if (_lastCommand == command)
                _event.Set();
        }

        public void Reset(string command)
        {
            _lastCommand = command;
            _event.Reset();
        }

        public void Clear()
        {
            _event.Dispose();
            _lastOperationSuccess = false;
        }

        public bool OperationResult
        {
            get { return _lastOperationSuccess; }
            set { _lastOperationSuccess = value; }
        }

        public void WaitingResponse()
        {
            while (!_event.Wait(10))
            {
                if (!_engine.IsTransportConnected)
                {
                    break;
                }

                Thread.Sleep(10);
            }
        }
    }

    internal sealed class RemoteState
    {
        private readonly Dictionary<int, ThreadInfo> _threadsInfo = new Dictionary<int, ThreadInfo>();
        private readonly SyncRequestManager _syncRequestManager;
        private static readonly ConcurrentDictionary<long, ProtocolVariable> _memoryVariables = new ConcurrentDictionary<long, ProtocolVariable>();

        private readonly AD7Engine _engine;

        public RemoteState(AD7Engine engine)
        {
            _engine = engine;
            _syncRequestManager = new SyncRequestManager(engine);
        }

        public Dictionary<int, ThreadInfo> ThreadsInfo => _threadsInfo;
        public static ConcurrentDictionary<long, ProtocolVariable> MemoryVariables => _memoryVariables;

        public SyncRequestManager SyncManager => _syncRequestManager;

        // Thread Management
        internal ThreadInfo GetOrCreateThreadInfo(int threadId)
        {
            if (_threadsInfo.TryGetValue(threadId, out var ti) && ti != null)
                return ti;

            ti = new ThreadInfo { Id = threadId, Name = "unknown name" };
            _threadsInfo[threadId] = ti;
            return ti;
        }

        internal AD7Thread GetOrCreateAd7Thread(int threadId)
        {
            var ti = GetOrCreateThreadInfo(threadId);
            if (ti.Ad7Thread != null)
                return ti.Ad7Thread;

            ti.Ad7Thread = new AD7Thread(_engine, threadId);
            return ti.Ad7Thread;
        }

        internal AD7Thread TryGetAd7Thread(int threadId)
        {
            if (threadId <= 0)
                return null;

            if (_threadsInfo.TryGetValue(threadId, out var ti))
                return ti.Ad7Thread;

            return null;
        }

        internal void RemoveAd7Thread(int threadId)
        {
            if (_threadsInfo.TryGetValue(threadId, out var ti))
                ti.Ad7Thread = null;
        }

        public void SetThreadName(int threadId, string name)
        {
            var ti = GetOrCreateThreadInfo(threadId);
            if (!string.IsNullOrEmpty(name))
                ti.Name = name;
        }

        public void RemoveThread(int threadId)
        {
            _threadsInfo.Remove(threadId);
        }

        // Runtime Data Management
        public void UpdateFrameVariablesAddr(int threadId, int frameId, List<long> variableAddrs)
        {
            var ti = GetOrCreateThreadInfo(threadId);
            if (!ti.Frames.TryGetValue(frameId, out var frame))
                return;

            frame.VariablesAddr.Clear();

            if (variableAddrs != null)
            {
                frame.VariablesAddr.AddRange(variableAddrs);
            }
        }

        public bool TryGetRuntimeVariablesAddr(int threadId, int frameId, out List<long> variablesAddr)
        {
            variablesAddr = null;
            if (_threadsInfo.TryGetValue(threadId, out var ti) && ti.Frames.TryGetValue(frameId, out var frame))
            {
                variablesAddr = frame.VariablesAddr;
                return true;
            }

            return false;
        }

        public void UpdateVariableChildrenAddr(long addr, List<long> childAddrs)
        {
            if (!MemoryVariables.TryGetValue(addr, out var v))
            {
                return;
            }

            v.Elements = new List<long>();
            if (childAddrs != null)
            {
                v.Elements.AddRange(childAddrs);
            }
        }

        public void AddVariableToFrame(int threadId, int frameId, long addr)
        {
            var ti = GetOrCreateThreadInfo(threadId);
            if (ti.Frames.TryGetValue(frameId, out var frame))
            {
                if (!frame.VariablesAddr.Contains(addr))
                {
                    frame.VariablesAddr.Add(addr);
                }
            }
        }

        // Frame Management
        public void UpdateFramesForThread(int threadId, List<FrameInfo> frames)
        {
            var ti = GetOrCreateThreadInfo(threadId);
            ti.Frames.Clear();
            if (frames != null)
            {
                foreach (var f in frames)
                {
                    ti.Frames[f.Id] = f;
                }
            }
        }

        // Reset Methods
        public void ResetAll()
        {
            foreach (var ti in _threadsInfo.Values)
            {
                ti.IsStopped = false;
                ti.StopReason = null;
                ti.CurrFile = null;
                ti.CurrLine = 0;
            }

            _threadsInfo.Clear();

            MemoryVariables.Clear();
        }

        public void ResetVariableTree()
        {
            foreach (var ti in _threadsInfo.Values)
            {
                foreach (var frame in ti.Frames.Values)
                {
                    frame.VariablesAddr.Clear();
                }
            }
        }

        public void ResetVariableTreeForThread(int threadId)
        {
            if (_threadsInfo.TryGetValue(threadId, out var ti))
            {
                foreach (var frame in ti.Frames.Values)
                {
                    frame.VariablesAddr.Clear();
                }

                ti.Frames.Clear();
            }
        }
    }
}
