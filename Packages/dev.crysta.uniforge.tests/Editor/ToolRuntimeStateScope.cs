using System;
using UniForge.TestRunner;

namespace UniForge.Tests
{
    internal sealed class ToolRuntimeStateScope : IDisposable
    {
        private readonly TestResultCacheSnapshot _testResultCacheSnapshot;
        private readonly PendingDomainReloadToolRequestsSnapshot _pendingRequestsSnapshot;

        public ToolRuntimeStateScope()
        {
            _testResultCacheSnapshot = TestResultCache.instance.CaptureState();
            _pendingRequestsSnapshot = PendingDomainReloadToolRequestsStorage.instance.CaptureState();
        }

        public void Dispose()
        {
            PendingDomainReloadToolRequestsStorage.instance.RestoreState(_pendingRequestsSnapshot);
            TestResultCache.instance.RestoreState(_testResultCacheSnapshot);
        }
    }
}
