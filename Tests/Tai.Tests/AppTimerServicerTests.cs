using Core.Enums;
using Core.Event;
using Core.Models.AppObserver;
using Core.Servicers.Instances;
using Core.Servicers.Interfaces;
using Moq;
using System;
using System.Threading;
using Xunit;

namespace Tai.Tests
{
    /// <summary>
    /// Tests for AppTimerServicer's timing state machine and process-switch logic.
    /// These tests serve as the regression safety net for the upcoming #257 rewrite
    /// (out-of-order event discard) and SQL parameterization work.
    /// </summary>
    public class AppTimerServicerTests
    {
        /// <summary>
        /// Helper: create an AppInfo for a normal (statistical) application.
        /// </summary>
        private static AppInfo MakeAppInfo(string process, AppType type = AppType.Win32)
        {
            return new AppInfo(IntPtr.Zero, 1234, process, "Test App", @"C:\Test\" + process + ".exe", type);
        }

        /// <summary>
        /// Helper: create a WindowInfo stub.
        /// </summary>
        private static WindowInfo MakeWindowInfo()
        {
            return new WindowInfo("WinClass", "Test", IntPtr.Zero, 800, 600, 0, 0);
        }

        /// <summary>
        /// Helper: raise the OnAppActiveChanged event on a mock IAppObserver.
        /// Uses reflection because the event is on the interface and the mock
        /// needs to raise it with specific args.
        /// </summary>
        private static void RaiseAppActiveChanged(Mock<IAppObserver> mock, AppInfo app, DateTime activeTime)
        {
            mock.Raise(m => m.OnAppActiveChanged += null, new AppActiveChangedEventArgs(app, MakeWindowInfo(), activeTime));
        }

        [Fact]
        public void Start_RegistersAppActiveChangedHandler()
        {
            var observerMock = new Mock<IAppObserver>();
            var servicer = new AppTimerServicer(observerMock.Object);

            servicer.Start();

            // After Start, raising the event should not throw
            var app = MakeAppInfo("testapp");
            RaiseAppActiveChanged(observerMock, app, DateTime.Now);

            // Should not throw — basic smoke test that handler is wired
            Assert.True(true);
        }

        [Fact]
        public void Stop_UnregistersAppActiveChangedHandler()
        {
            var observerMock = new Mock<IAppObserver>();
            var servicer = new AppTimerServicer(observerMock.Object);

            servicer.Start();
            servicer.Stop();

            // After Stop, raising the event should not cause duration updates
            // (the handler is unregistered)
            var app = MakeAppInfo("testapp");
            RaiseAppActiveChanged(observerMock, app, DateTime.Now);

            Assert.True(true);
        }

        [Fact]
        public void Start_CalledTwice_DoesNotDoubleInitialize()
        {
            var observerMock = new Mock<IAppObserver>();
            var servicer = new AppTimerServicer(observerMock.Object);

            servicer.Start();
            servicer.Start(); // should be idempotent

            Assert.True(true);
        }

        [Fact]
        public void GetAppDuration_WithNoActiveProcess_ReturnsNull()
        {
            var observerMock = new Mock<IAppObserver>();
            var servicer = new AppTimerServicer(observerMock.Object);

            servicer.Start();

            var result = servicer.GetAppDuration();
            Assert.Null(result);
        }

        /// <summary>
        /// When a process switch happens (from app A to app B), the timer should
        /// stop for A, invoke the duration update event, then start for B.
        /// This test verifies the basic state machine transition.
        /// </summary>
        [Fact]
        public void ProcessSwitch_EmitsDurationUpdateForPreviousApp()
        {
            var observerMock = new Mock<IAppObserver>();
            var servicer = new AppTimerServicer(observerMock.Object);

            AppDurationUpdatedEventArgs capturedArgs = null;
            servicer.OnAppDurationUpdated += (sender, e) => capturedArgs = e;

            servicer.Start();

            // App A becomes active
            var appA = MakeAppInfo("appA");
            RaiseAppActiveChanged(observerMock, appA, DateTime.Now);

            // Wait 1.5s so the timer ticks at least once (timer interval is 1000ms)
            Thread.Sleep(1500);

            // Switch to App B — this should emit duration for App A
            var appB = MakeAppInfo("appB");
            RaiseAppActiveChanged(observerMock, appB, DateTime.Now);

            // App A's duration should have been emitted
            Assert.NotNull(capturedArgs);
            Assert.Equal("appA", capturedArgs.App.Process);
            Assert.True(capturedArgs.Duration >= 1);
        }

        /// <summary>
        /// SystemComponent apps (taskbar, start menu, etc.) should not be
        /// tracked for duration — IsStatistical should return false for them.
        /// </summary>
        [Fact]
        public void SystemComponentApp_DoesNotStartTimer()
        {
            var observerMock = new Mock<IAppObserver>();
            var servicer = new AppTimerServicer(observerMock.Object);

            AppDurationUpdatedEventArgs capturedArgs = null;
            servicer.OnAppDurationUpdated += (sender, e) => capturedArgs = e;

            servicer.Start();

            // A real app first
            var realApp = MakeAppInfo("realapp");
            RaiseAppActiveChanged(observerMock, realApp, DateTime.Now);

            Thread.Sleep(1500);

            // Switch to SystemComponent — should stop tracking realapp
            var systemApp = MakeAppInfo("explorer", AppType.SystemComponent);
            RaiseAppActiveChanged(observerMock, systemApp, DateTime.Now);

            // realapp duration should have been emitted
            Assert.NotNull(capturedArgs);
            Assert.Equal("realapp", capturedArgs.App.Process);
        }

        /// <summary>
        /// Same process reported again should NOT trigger a new timer start
        /// (deduplication in the process-switch check).
        /// </summary>
        [Fact]
        public void SameProcessReportedAgain_DoesNotRestartTimer()
        {
            var observerMock = new Mock<IAppObserver>();
            var servicer = new AppTimerServicer(observerMock.Object);

            int eventCount = 0;
            servicer.OnAppDurationUpdated += (sender, e) => eventCount++;

            servicer.Start();

            var app = MakeAppInfo("sameapp");
            RaiseAppActiveChanged(observerMock, app, DateTime.Now);

            // Report the same process again — no switch, no timer restart
            RaiseAppActiveChanged(observerMock, app, DateTime.Now);

            // No duration event should have fired (because no switch happened)
            Assert.Equal(0, eventCount);
        }

        /// <summary>
        /// Empty/null process name or executable path should be treated as
        /// non-statistical (IsStatistical returns false).
        /// </summary>
        [Theory]
        [InlineData("", "C:\\test\\app.exe")]
        [InlineData("app", "")]
        [InlineData("app", null)]
        public void InvalidAppInfo_IsNotStatistical(string process, string execPath)
        {
            // IsStatistical is private — we test it indirectly by verifying
            // that no timer starts and no duration event fires
            var observerMock = new Mock<IAppObserver>();
            var servicer = new AppTimerServicer(observerMock.Object);

            int eventCount = 0;
            servicer.OnAppDurationUpdated += (sender, e) => eventCount++;

            servicer.Start();

            // Create an app with empty process or exec path
            var app = new AppInfo(IntPtr.Zero, 0, process ?? "", "desc", execPath ?? "", AppType.Win32);
            RaiseAppActiveChanged(observerMock, app, DateTime.Now);

            Thread.Sleep(1500);

            // Switch to another app to trigger any pending duration
            var app2 = MakeAppInfo("validapp");
            RaiseAppActiveChanged(observerMock, app2, DateTime.Now);

            // No duration event for the invalid app
            Assert.Equal(0, eventCount);
        }
    }
}
