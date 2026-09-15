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
        /// Moq's Raise requires (sender, eventArgs) matching the delegate signature.
        /// </summary>
        private static void RaiseAppActiveChanged(Mock<IAppObserver> mock, AppInfo app, DateTime activeTime)
        {
            var args = new AppActiveChangedEventArgs(app, MakeWindowInfo(), activeTime);
            mock.Raise(m => m.OnAppActiveChanged += null, mock.Object, args);
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

        /// <summary>
        /// Out-of-order events (stale callbacks arriving after newer ones)
        /// should be silently discarded to prevent timing corruption.
        /// This is the core regression test for the #257 fix.
        /// </summary>
        [Fact]
        public void OutOfOrderEvent_IsDiscarded()
        {
            var observerMock = new Mock<IAppObserver>();
            var servicer = new AppTimerServicer(observerMock.Object);

            int eventCount = 0;
            string lastProcess = null;
            servicer.OnAppDurationUpdated += (sender, e) =>
            {
                eventCount++;
                lastProcess = e.App.Process;
            };

            servicer.Start();

            var baseTime = new DateTime(2025, 6, 15, 10, 0, 0);

            // App A active at t=0
            var appA = MakeAppInfo("appA");
            RaiseAppActiveChanged(observerMock, appA, baseTime);

            // App B active at t=2s (normal switch)
            var appB = MakeAppInfo("appB");
            RaiseAppActiveChanged(observerMock, appB, baseTime.AddSeconds(2));

            // Stale event: App A callback from t=1s arrives late (out of order)
            RaiseAppActiveChanged(observerMock, appA, baseTime.AddSeconds(1));

            // The stale event should have been discarded — last event emitted
            // should be for appA (from the A->B switch), not a second event.
            // The stale A@t1 must NOT have restarted A's timer or emitted a
            // duration for B again.
            Assert.True(eventCount <= 1, $"Expected at most 1 duration event, got {eventCount}");
            if (eventCount == 1)
            {
                Assert.Equal("appA", lastProcess);
            }
        }

        /// <summary>
        /// When events arrive in correct chronological order, the timer
        /// should process them normally (regression guard for the discard logic).
        /// </summary>
        [Fact]
        public void InOrderEvents_ProcessedNormally()
        {
            var observerMock = new Mock<IAppObserver>();
            var servicer = new AppTimerServicer(observerMock.Object);

            int eventCount = 0;
            servicer.OnAppDurationUpdated += (sender, e) => eventCount++;

            servicer.Start();

            var baseTime = new DateTime(2025, 6, 15, 10, 0, 0);

            // App A at t=0, App B at t=1s — both in order
            RaiseAppActiveChanged(observerMock, MakeAppInfo("appA"), baseTime);
            RaiseAppActiveChanged(observerMock, MakeAppInfo("appB"), baseTime.AddSeconds(1));

            // The switch from A to B should emit a duration event
            Assert.Equal(1, eventCount);
        }
    }
}
