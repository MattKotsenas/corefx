// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.DotNet.RemoteExecutor;
using Xunit;

namespace System.Diagnostics.Tests
{
    public class ProcessWaitingTests : ProcessTestBase
    {
        [Fact]
        public void MultipleProcesses_StartAllKillAllWaitAll()
        {
            const int Iters = 10;
            Process[] processes = Enumerable.Range(0, Iters).Select(_ => CreateProcessLong()).ToArray();

            foreach (Process p in processes) p.Start();
            foreach (Process p in processes) p.Kill();
            foreach (Process p in processes) Assert.True(p.WaitForExit(WaitInMS));
        }

        [Fact]
        public async Task MultipleProcesses_StartAllKillAllWaitAllAsync()
        {
            const int Iters = 10;
            Process[] processes = Enumerable.Range(0, Iters).Select(_ => CreateProcessLong()).ToArray();

            foreach (Process p in processes) p.EnableRaisingEvents = true;
            foreach (Process p in processes) p.Start();
            foreach (Process p in processes) p.Kill();
            foreach (Process p in processes)
            {
                using (var cts = new CancellationTokenSource(WaitInMS))
                {
                    var task = p.WaitForExitAsync(cts.Token);
                    await task;
                    Assert.True(task.IsCompletedSuccessfully);
                    Assert.False(task.IsCanceled);
                    Assert.True(p.HasExited);
                }
            }
        }

        [Fact]
        public void MultipleProcesses_SerialStartKillWait()
        {
            const int Iters = 10;
            for (int i = 0; i < Iters; i++)
            {
                Process p = CreateProcessLong();
                p.Start();
                p.Kill();
                Assert.True(p.WaitForExit(WaitInMS));
            }
        }

        [Fact]
        public async Task MultipleProcesses_SerialStartKillWaitAsync()
        {
            const int Iters = 10;
            for (int i = 0; i < Iters; i++)
            {
                Process p = CreateProcessLong();
                p.EnableRaisingEvents = true;
                p.Start();
                p.Kill();
                using (var cts = new CancellationTokenSource(WaitInMS))
                {
                    var task = p.WaitForExitAsync(cts.Token);
                    await task;
                    Assert.True(task.IsCompletedSuccessfully);
                    Assert.False(task.IsCanceled);
                    Assert.True(p.HasExited);
                }
            }
        }

        [Fact]
        public void MultipleProcesses_ParallelStartKillWait()
        {
            const int Tasks = 4, ItersPerTask = 10;
            Action work = () =>
            {
                for (int i = 0; i < ItersPerTask; i++)
                {
                    Process p = CreateProcessLong();
                    p.Start();
                    p.Kill();
                    p.WaitForExit(WaitInMS);
                }
            };
            Task.WaitAll(Enumerable.Range(0, Tasks).Select(_ => Task.Run(work)).ToArray());
        }

        [Fact]
        public async Task MultipleProcesses_ParallelStartKillWaitAsync()
        {
            const int Tasks = 4, ItersPerTask = 10;
            Action work = async () =>
            {
                for (int i = 0; i < ItersPerTask; i++)
                {
                    Process p = CreateProcessLong();
                    p.EnableRaisingEvents = true;
                    p.Start();
                    p.Kill();
                    using (var cts = new CancellationTokenSource(WaitInMS))
                    {
                        var task = p.WaitForExitAsync(cts.Token);
                        await task;
                        Assert.True(task.IsCompletedSuccessfully);
                        Assert.False(task.IsCanceled);
                        Assert.True(p.HasExited);
                    }
                }
            };

            await Task.WhenAll(Enumerable.Range(0, Tasks).Select(_ => Task.Run(work)).ToArray());
        }

        [Theory]
        [InlineData(0)]  // poll
        [InlineData(10)] // real timeout
        public void CurrentProcess_WaitNeverCompletes(int milliseconds)
        {
            Assert.False(Process.GetCurrentProcess().WaitForExit(milliseconds));
        }

        [Theory]
        [InlineData(0)]  // poll
        [InlineData(10)] // real timeout
        public async Task CurrentProcess_WaitAsyncNeverCompletes(int milliseconds)
        {
            using (var cts = new CancellationTokenSource(milliseconds))
            {
                var process = Process.GetCurrentProcess();
                process.EnableRaisingEvents = true;
                var task = process.WaitForExitAsync(cts.Token);
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
                Assert.False(task.IsCompletedSuccessfully);
                Assert.True(task.IsCanceled);
                Assert.False(process.HasExited);
            }
        }

        [Fact]
        public void SingleProcess_TryWaitMultipleTimesBeforeCompleting()
        {
            Process p = CreateProcessLong();
            p.Start();

            // Verify we can try to wait for the process to exit multiple times
            Assert.False(p.WaitForExit(0));
            Assert.False(p.WaitForExit(0));

            // Then wait until it exits and concurrently kill it.
            // There's a race condition here, in that we really want to test
            // killing it while we're waiting, but we could end up killing it
            // before hand, in which case we're simply not testing exactly
            // what we wanted to test, but everything should still work.
            Task.Delay(10).ContinueWith(_ => p.Kill());
            Assert.True(p.WaitForExit(WaitInMS));
            Assert.True(p.WaitForExit(0));
        }

        [Fact]
        public async Task SingleProcess_TryWaitAsyncMultipleTimesBeforeCompleting()
        {
            Process p = CreateProcessLong();
            p.EnableRaisingEvents = true;
            p.Start();

            // Verify we can try to wait for the process to exit multiple times
            using (var cts = new CancellationTokenSource(0))
            {
                for (var i = 0; i < 2; i++)
                {
                    var task = p.WaitForExitAsync(cts.Token);
                    await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
                    Assert.False(task.IsCompletedSuccessfully);
                    Assert.True(task.IsCanceled);
                    Assert.False(p.HasExited);
                }
            }

            // Then wait until it exits and concurrently kill it.
            // There's a race condition here, in that we really want to test
            // killing it while we're waiting, but we could end up killing it
            // before hand, in which case we're simply not testing exactly
            // what we wanted to test, but everything should still work.
#pragma warning disable CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
            Task.Delay(10).ContinueWith(_ => p.Kill());
#pragma warning restore CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed

            using (var cts = new CancellationTokenSource(WaitInMS))
            {
                var task = p.WaitForExitAsync(cts.Token);
                await task;
                Assert.True(task.IsCompletedSuccessfully);
                Assert.False(task.IsCanceled);
                Assert.True(p.HasExited);
            }
            using (var cts = new CancellationTokenSource(0))
            {
                var task = p.WaitForExitAsync(cts.Token);
                await task;
                Assert.True(task.IsCompletedSuccessfully);
                Assert.False(task.IsCanceled);
                Assert.True(p.HasExited);
            }
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task SingleProcess_WaitAfterExited(bool addHandlerBeforeStart)
        {
            Process p = CreateProcessLong();
            p.EnableRaisingEvents = true;

            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            if (addHandlerBeforeStart)
            {
                p.Exited += delegate { tcs.SetResult(true); };
            }
            p.Start();
            if (!addHandlerBeforeStart)
            {
                p.Exited += delegate { tcs.SetResult(true); };
            }

            p.Kill();
            Assert.True(await tcs.Task);

            Assert.True(p.WaitForExit(0));
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task SingleProcess_WaitAsyncAfterExited(bool addHandlerBeforeStart)
        {
            Process p = CreateProcessLong();
            p.EnableRaisingEvents = true;

            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            if (addHandlerBeforeStart)
            {
                p.Exited += delegate
                { tcs.SetResult(true); };
            }
            p.Start();
            if (!addHandlerBeforeStart)
            {
                p.Exited += delegate
                { tcs.SetResult(true); };
            }

            p.Kill();
            Assert.True(await tcs.Task);

            using (var cts = new CancellationTokenSource(0))
            {
                var task = p.WaitForExitAsync(cts.Token);
                await task;
                Assert.True(task.IsCompletedSuccessfully);
                Assert.False(task.IsCanceled);
                Assert.True(p.HasExited);
            }

            {
                var task = p.WaitForExitAsync();
                await task;
                Assert.True(task.IsCompletedSuccessfully);
                Assert.False(task.IsCanceled);
                Assert.True(p.HasExited);
            }
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(127)]
        public async Task SingleProcess_EnableRaisingEvents_CorrectExitCode(int exitCode)
        {
            using (Process p = CreateProcessPortable(RemotelyInvokable.ExitWithCode, exitCode.ToString()))
            {
                var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                p.EnableRaisingEvents = true;
                p.Exited += delegate
                { tcs.SetResult(true); };
                p.Start();
                Assert.True(await tcs.Task);
                Assert.Equal(exitCode, p.ExitCode);
            }
        }

        [Fact]
        public void SingleProcess_CopiesShareExitInformation()
        {
            Process p = CreateProcessLong();
            p.Start();

            Process[] copies = Enumerable.Range(0, 3).Select(_ => Process.GetProcessById(p.Id)).ToArray();

            Assert.False(p.WaitForExit(0));
            p.Kill();
            Assert.True(p.WaitForExit(WaitInMS));

            foreach (Process copy in copies)
            {
                Assert.True(copy.WaitForExit(0));
            }
        }

        [Fact]
        public async Task SingleProcess_CopiesShareExitAsyncInformation()
        {
            Process p = CreateProcessLong();
            p.EnableRaisingEvents = true;
            p.Start();

            Process[] copies = Enumerable.Range(0, 3).Select(_ =>
            {
                var copy = Process.GetProcessById(p.Id);
                copy.EnableRaisingEvents = true;
                return copy;
            }).ToArray();

            using (var cts = new CancellationTokenSource(0))
            {
                var task = p.WaitForExitAsync(cts.Token);
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
                Assert.False(task.IsCompletedSuccessfully);
                Assert.True(task.IsCanceled);
                Assert.False(p.HasExited);
            }
            p.Kill();
            using (var cts = new CancellationTokenSource(WaitInMS))
            {
                var task = p.WaitForExitAsync(cts.Token);
                await task;
                Assert.True(task.IsCompletedSuccessfully);
                Assert.False(task.IsCanceled);
                Assert.True(p.HasExited);
            }

            using (var cts = new CancellationTokenSource(0))
            {
                foreach (Process copy in copies)
                {
                    var task = copy.WaitForExitAsync(cts.Token);
                    await task;
                    Assert.True(task.IsCompletedSuccessfully);
                    Assert.False(task.IsCanceled);
                    Assert.True(copy.HasExited);
                }
            }
        }

        [Fact]
        public void WaitForPeerProcess()
        {
            Process child1 = CreateProcessLong();
            child1.Start();

            Process child2 = CreateProcess(peerId =>
            {
                Process peer = Process.GetProcessById(int.Parse(peerId));
                Console.WriteLine("Signal");
                Assert.True(peer.WaitForExit(WaitInMS));
                return RemoteExecutor.SuccessExitCode;
            }, child1.Id.ToString());
            child2.StartInfo.RedirectStandardOutput = true;
            child2.Start();
            char[] output = new char[6];
            child2.StandardOutput.Read(output, 0, output.Length);
            Assert.Equal("Signal", new string(output)); // wait for the signal before killing the peer

            child1.Kill();
            Assert.True(child1.WaitForExit(WaitInMS));
            Assert.True(child2.WaitForExit(WaitInMS));

            Assert.Equal(RemoteExecutor.SuccessExitCode, child2.ExitCode);
        }

        [Fact]
        public async Task WaitAsyncForPeerProcess()
        {
            Process child1 = CreateProcessLong();
            child1.EnableRaisingEvents = true;
            child1.Start();

            Process child2 = CreateProcess(async peerId =>
            {
                Process peer = Process.GetProcessById(int.Parse(peerId));
                peer.EnableRaisingEvents = true;
                Console.WriteLine("Signal");
                using (var cts = new CancellationTokenSource(WaitInMS))
                {
                    var task = peer.WaitForExitAsync(cts.Token);
                    await task;
                    Assert.True(task.IsCompletedSuccessfully);
                    Assert.False(task.IsCanceled);
                    Assert.True(peer.HasExited);
                }
                return RemoteExecutor.SuccessExitCode;
            }, child1.Id.ToString());
            child2.StartInfo.RedirectStandardOutput = true;
            child2.EnableRaisingEvents = true;
            child2.Start();
            char[] output = new char[6];
            child2.StandardOutput.Read(output, 0, output.Length);
            Assert.Equal("Signal", new string(output)); // wait for the signal before killing the peer

            child1.Kill();
            using (var cts = new CancellationTokenSource(WaitInMS))
            {
                var task = child1.WaitForExitAsync(cts.Token);
                await task;
                Assert.True(task.IsCompletedSuccessfully);
                Assert.False(task.IsCanceled);
                Assert.True(child1.HasExited);
            }
            using (var cts = new CancellationTokenSource(WaitInMS))
            {
                var task = child2.WaitForExitAsync(cts.Token);
                await task;
                Assert.True(task.IsCompletedSuccessfully);
                Assert.False(task.IsCanceled);
                Assert.True(child2.HasExited);
            }

            Assert.Equal(RemoteExecutor.SuccessExitCode, child2.ExitCode);
        }

        [Fact]
        public void WaitForSignal()
        {
            const string expectedSignal = "Signal";
            const string successResponse = "Success";
            const int timeout = 30 * 1000; // 30 seconds, to allow for very slow machines

            Process p = CreateProcessPortable(RemotelyInvokable.WriteLineReadLine);
            p.StartInfo.RedirectStandardInput = true;
            p.StartInfo.RedirectStandardOutput = true;
            var mre = new ManualResetEventSlim(false);

            int linesReceived = 0;
            p.OutputDataReceived += (s, e) =>
            {
                if (e.Data != null)
                {
                    linesReceived++;

                    if (e.Data == expectedSignal)
                    {
                        mre.Set();
                    }
                }
            };

            p.Start();
            p.BeginOutputReadLine();

            Assert.True(mre.Wait(timeout));
            Assert.Equal(1, linesReceived);

            // Wait a little bit to make sure process didn't exit on itself
            Thread.Sleep(100);
            Assert.False(p.HasExited, "Process has prematurely exited");

            using (StreamWriter writer = p.StandardInput)
            {
                writer.WriteLine(successResponse);
            }

            Assert.True(p.WaitForExit(timeout), "Process has not exited");
            Assert.Equal(RemotelyInvokable.SuccessExitCode, p.ExitCode);
        }

        [Fact]
        public async Task WaitAsyncForSignal()
        {
            const string expectedSignal = "Signal";
            const string successResponse = "Success";
            const int timeout = 5 * 1000;

            Process p = CreateProcessPortable(RemotelyInvokable.WriteLineReadLine);
            p.StartInfo.RedirectStandardInput = true;
            p.StartInfo.RedirectStandardOutput = true;
            var mre = new ManualResetEventSlim(false);

            int linesReceived = 0;
            p.OutputDataReceived += (s, e) =>
            {
                if (e.Data != null)
                {
                    linesReceived++;

                    if (e.Data == expectedSignal)
                    {
                        mre.Set();
                    }
                }
            };

            p.EnableRaisingEvents = true;
            p.Start();
            p.BeginOutputReadLine();

            Assert.True(mre.Wait(timeout));
            Assert.Equal(1, linesReceived);

            // Wait a little bit to make sure process didn't exit on itself
            Thread.Sleep(100);
            Assert.False(p.HasExited, "Process has prematurely exited");

            using (StreamWriter writer = p.StandardInput)
            {
                writer.WriteLine(successResponse);
            }

            using (var cts = new CancellationTokenSource(timeout))
            {
                var task = p.WaitForExitAsync(cts.Token);
                await task;
                Assert.True(task.IsCompletedSuccessfully);
                Assert.False(task.IsCanceled);
                Assert.True(p.HasExited, "Process has not exited");
            }
            Assert.Equal(RemotelyInvokable.SuccessExitCode, p.ExitCode);
        }

        [Fact]
        [ActiveIssue(15844, TestPlatforms.AnyUnix)]
        public void WaitChain()
        {
            Process root = CreateProcess(() =>
            {
                Process child1 = CreateProcess(() =>
                {
                    Process child2 = CreateProcess(() =>
                    {
                        Process child3 = CreateProcess(() => RemoteExecutor.SuccessExitCode);
                        child3.Start();
                        Assert.True(child3.WaitForExit(WaitInMS));
                        return child3.ExitCode;
                    });
                    child2.Start();
                    Assert.True(child2.WaitForExit(WaitInMS));
                    return child2.ExitCode;
                });
                child1.Start();
                Assert.True(child1.WaitForExit(WaitInMS));
                return child1.ExitCode;
            });
            root.Start();
            Assert.True(root.WaitForExit(WaitInMS));
            Assert.Equal(RemoteExecutor.SuccessExitCode, root.ExitCode);
        }

        [Fact]
        [ActiveIssue(15844, TestPlatforms.AnyUnix)]
        public async Task WaitAsyncChain()
        {
            Process root = CreateProcess(async () =>
            {
                Process child1 = CreateProcess(async () =>
                {
                    Process child2 = CreateProcess(async () =>
                    {
                        Process child3 = CreateProcess(() => SuccessExitCode);
                        child3.EnableRaisingEvents = true;
                        child3.Start();
                        using (var cts = new CancellationTokenSource(WaitInMS))
                        {
                            var task = child3.WaitForExitAsync(cts.Token);
                            await task;
                            Assert.True(task.IsCompletedSuccessfully);
                            Assert.False(task.IsCanceled);
                            Assert.True(child3.HasExited);
                        }

                        return child3.ExitCode;
                    });
                    child2.EnableRaisingEvents = true;
                    child2.Start();
                    using (var cts = new CancellationTokenSource(WaitInMS))
                    {
                        var task = child2.WaitForExitAsync(cts.Token);
                        await task;
                        Assert.True(task.IsCompletedSuccessfully);
                        Assert.False(task.IsCanceled);
                        Assert.True(child2.HasExited);
                    }

                    return child2.ExitCode;
                });
                child1.EnableRaisingEvents = true;
                child1.Start();
                using (var cts = new CancellationTokenSource(WaitInMS))
                {
                    var task = child1.WaitForExitAsync(cts.Token);
                    await task;
                    Assert.True(task.IsCompletedSuccessfully);
                    Assert.False(task.IsCanceled);
                    Assert.True(child1.HasExited);
                }

                return child1.ExitCode;
            });
            root.EnableRaisingEvents = true;
            root.Start();
            using (var cts = new CancellationTokenSource(WaitInMS))
            {
                var task = root.WaitForExitAsync(cts.Token);
                await task;
                Assert.True(task.IsCompletedSuccessfully);
                Assert.False(task.IsCanceled);
                Assert.True(root.HasExited);
            }
            Assert.Equal(RemoteExecutor.SuccessExitCode, root.ExitCode);
        }

        [Fact]
        public void WaitForSelfTerminatingChild()
        {
            Process child = CreateProcessPortable(RemotelyInvokable.SelfTerminate);
            child.Start();
            Assert.True(child.WaitForExit(WaitInMS));
            Assert.NotEqual(RemoteExecutor.SuccessExitCode, child.ExitCode);
        }

        [Fact]
        public async Task WaitAsyncForSelfTerminatingChild()
        {
            Process child = CreateProcessPortable(RemotelyInvokable.SelfTerminate);
            child.EnableRaisingEvents = true;
            child.Start();
            using (var cts = new CancellationTokenSource(WaitInMS))
            {
                var task = child.WaitForExitAsync(cts.Token);
                await task;
                Assert.True(task.IsCompletedSuccessfully);
                Assert.False(task.IsCanceled);
                Assert.True(child.HasExited);
            }
            Assert.NotEqual(SuccessExitCode, child.ExitCode);
        }

        [Fact]
        public void WaitForInputIdle_NotDirected_ThrowsInvalidOperationException()
        {
            var process = new Process();
            Assert.Throws<InvalidOperationException>(() => process.WaitForInputIdle());
        }

        [Fact]
        public void WaitForExit_NotDirected_ThrowsInvalidOperationException()
        {
            var process = new Process();
            Assert.Throws<InvalidOperationException>(() => process.WaitForExit());
        }

        [Fact]
        public async Task WaitForExitAsync_NotDirected_ThrowsInvalidOperationException()
        {
            var process = new Process();
            await Assert.ThrowsAsync<InvalidOperationException>(() => process.WaitForExitAsync());
        }

        [Fact]
        public async Task WaitForExitAsync_NotEnabledRaisingEvents_ThrowsInvalidOperationException()
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => Process.GetCurrentProcess().WaitForExitAsync());
        }
    }
}
