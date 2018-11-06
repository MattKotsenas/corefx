// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.
using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Collections.Generic;
using System.Threading.Tasks;
using Xunit;

// TODO: MATTKOT: Use a sleep / signal in the process, along with Task.Run() to test races where cancellation happens during std out read to ensure that there aren't read races

namespace System.Diagnostics.Tests
{
    public partial class ProcessStreamReadTests : ProcessTestBase
    {
        [Fact]
        public void TestSyncErrorStream()
        {
            Process p = CreateProcessPortable(RemotelyInvokable.ErrorProcessBody);
            p.StartInfo.RedirectStandardError = true;
            p.Start();
            string expected = RemotelyInvokable.TestConsoleApp + " started error stream" + Environment.NewLine +
                              RemotelyInvokable.TestConsoleApp + " closed error stream" + Environment.NewLine;
            Assert.Equal(expected, p.StandardError.ReadToEnd());
            Assert.True(p.WaitForExit(WaitInMS));
        }

        [Fact]
        public void TestAsyncErrorStream()
        {
            for (int i = 0; i < 2; ++i)
            {
                StringBuilder sb = new StringBuilder();
                Process p = CreateProcessPortable(RemotelyInvokable.ErrorProcessBody);
                p.StartInfo.RedirectStandardError = true;
                p.ErrorDataReceived += (s, e) =>
                {
                    sb.Append(e.Data);
                    if (i == 1)
                    {
                        ((Process)s).CancelErrorRead();
                    }
                };
                p.Start();
                p.BeginErrorReadLine();

                Assert.True(p.WaitForExit(WaitInMS));
                p.WaitForExit(); // This ensures async event handlers are finished processing.

                string expected = RemotelyInvokable.TestConsoleApp + " started error stream" + (i == 1 ? "" : RemotelyInvokable.TestConsoleApp + " closed error stream");
                Assert.Equal(expected, sb.ToString());
            }
        }

        [Fact]
        public async Task TestAsyncErrorStreamAsync()
        {
            Process p = CreateProcessPortable(RemotelyInvokable.ErrorProcessBody);
            p.StartInfo.RedirectStandardError = true;

            using (var cts = new CancellationTokenSource())
            {
                cts.CancelAfter(WaitInMS);
                using (var result = await p.StartAndWaitForExitAsync(cts.Token))
                {
                    Assert.True(result.Exited);

                    string expected = RemotelyInvokable.TestConsoleApp + " started error stream" + Environment.NewLine +
                                      RemotelyInvokable.TestConsoleApp + " closed error stream" + Environment.NewLine;
                    Assert.Equal(expected, result.StandardError.ReadToEnd());
                }
            }
        }

        [Fact]
        public void TestSyncOutputStream()
        {
            Process p = CreateProcessPortable(RemotelyInvokable.StreamBody);
            p.StartInfo.RedirectStandardOutput = true;
            p.Start();
            string s = p.StandardOutput.ReadToEnd();
            Assert.True(p.WaitForExit(WaitInMS));
            Assert.Equal(RemotelyInvokable.TestConsoleApp + " started" + Environment.NewLine + RemotelyInvokable.TestConsoleApp + " closed" + Environment.NewLine, s);
        }

        [Fact]
        public void TestAsyncOutputStream()
        {
            for (int i = 0; i < 2; ++i)
            {
                StringBuilder sb = new StringBuilder();
                Process p = CreateProcessPortable(RemotelyInvokable.StreamBody);
                p.StartInfo.RedirectStandardOutput = true;
                p.OutputDataReceived += (s, e) =>
                {
                    sb.Append(e.Data);
                    if (i == 1)
                    {
                        ((Process)s).CancelOutputRead();
                    }
                };
                p.Start();
                p.BeginOutputReadLine();
                Assert.True(p.WaitForExit(WaitInMS));
                p.WaitForExit(); // This ensures async event handlers are finished processing.

                string expected = RemotelyInvokable.TestConsoleApp + " started" + (i == 1 ? "" : RemotelyInvokable.TestConsoleApp + " closed");
                Assert.Equal(expected, sb.ToString());
            }
        }

        [Fact]
        public async Task TestAsyncOutputStreamAsync()
        {
            Process p = CreateProcessPortable(RemotelyInvokable.StreamBody);
            p.StartInfo.RedirectStandardOutput = true;

            using (var cts = new CancellationTokenSource())
            {
                cts.CancelAfter(WaitInMS);
                using (var result = await p.StartAndWaitForExitAsync(cts.Token))
                {
                    Assert.True(result.Exited);

                    string expected = RemotelyInvokable.TestConsoleApp + " started" + Environment.NewLine +
                                      RemotelyInvokable.TestConsoleApp + " closed" + Environment.NewLine;
                    Assert.Equal(expected, result.StandardOutput.ReadToEnd());
                }
            }
        }

        [Fact]
        public void TestSyncStreams()
        {
            const string expected = "This string should come as output";
            Process p = CreateProcessPortable(RemotelyInvokable.ReadLine);
            p.StartInfo.RedirectStandardInput = true;
            p.StartInfo.RedirectStandardOutput = true;
            p.OutputDataReceived += (s, e) => { Assert.Equal(expected, e.Data); };
            p.Start();
            using (StreamWriter writer = p.StandardInput)
            {
                writer.WriteLine(expected);
            }
            Assert.True(p.WaitForExit(WaitInMS));
        }

        [Fact]
        public void TestEOFReceivedWhenStdInClosed()
        {
            // This is the test for the fix of dotnet/corefx issue #13447.
            //
            // Summary of the issue:
            // When an application starts more than one child processes with their standard inputs redirected on Unix,
            // closing the standard input stream of the first child process won't unblock the 'Console.ReadLine()' call
            // in the first child process (it's expected to receive EOF).
            //
            // Root cause of the issue:
            // The file descriptor for the write end of the first child process standard input redirection pipe gets
            // inherited by the second child process, which makes the reference count of the pipe write end become 2.
            // When closing the standard input stream of the first child process, the file descriptor held by the parent
            // process is released, but the one inherited by the second child process is still referencing the pipe
            // write end, which cause the 'Console.ReadLine()' continue to be blocked in the first child process.
            //
            // Fix:
            // Set the O_CLOEXEC flag when creating the redirection pipes. So that no child process would inherit the
            // file descriptors referencing those pipes.
            const string ExpectedLine = "NULL";
            Process p1 = CreateProcessPortable(RemotelyInvokable.ReadLineWriteIfNull);
            Process p2 = CreateProcessPortable(RemotelyInvokable.ReadLine);

            // Start the first child process
            p1.StartInfo.RedirectStandardInput = true;
            p1.StartInfo.RedirectStandardOutput = true;
            p1.OutputDataReceived += (s, e) => Assert.Equal(ExpectedLine, e.Data);
            p1.Start();

            // Start the second child process
            p2.StartInfo.RedirectStandardInput = true;
            p2.Start();

            try
            {
                // Close the standard input stream of the first child process.
                // The first child process should be unblocked and write out 'NULL', and then exit.
                p1.StandardInput.Close();
                Assert.True(p1.WaitForExit(WaitInMS));
            }
            finally
            {
                // Cleanup: kill the second child process
                p2.Kill();
            }

            // Cleanup
            Assert.True(p2.WaitForExit(WaitInMS));
            p2.Dispose();
            p1.Dispose();
        }

        [Fact]
        [SkipOnTargetFramework(TargetFrameworkMonikers.NetFramework, "There is 2 bugs in Desktop in this codepath, see: dotnet/corefx #18437 and #18436")]
        [SkipOnTargetFramework(TargetFrameworkMonikers.Uap, "No simple way to perform this on uap using cmd.exe")]
        public void TestAsyncHalfCharacterAtATime()
        {
            var receivedOutput = false;
            var collectedExceptions = new List<Exception>();

            Process p = CreateProcessPortable(RemotelyInvokable.WriteSlowlyByByte);
            p.StartInfo.RedirectStandardOutput = true;
            p.StartInfo.StandardOutputEncoding = Encoding.Unicode;
            p.OutputDataReceived += (s, e) =>
            {
                try
                {
                    if (!receivedOutput)
                    {
                        receivedOutput = true;
                        Assert.Equal("a", e.Data);
                    }
                }
                catch (Exception ex)
                {
                    // This ensures that the exception in event handlers does not break
                    // the whole unittest
                    collectedExceptions.Add(ex);
                }
            };
            p.Start();
            p.BeginOutputReadLine();

            Assert.True(p.WaitForExit(WaitInMS));
            p.WaitForExit(); // This ensures async event handlers are finished processing.

            Assert.True(receivedOutput);

            if (collectedExceptions.Count > 0)
            {
                // Re-throw collected exceptions
                throw new AggregateException(collectedExceptions);
            }
        }

        [Fact]
        [SkipOnTargetFramework(TargetFrameworkMonikers.NetFramework, "There is 2 bugs in Desktop in this codepath, see: dotnet/corefx #18437 and #18436")]
        [SkipOnTargetFramework(TargetFrameworkMonikers.Uap, "No simple way to perform this on uap using cmd.exe")]
        public async Task TestAsyncHalfCharacterAtATimeAsync()
        {
            Process p = CreateProcessPortable(RemotelyInvokable.WriteSlowlyByByte);
            p.StartInfo.RedirectStandardOutput = true;
            p.StartInfo.StandardOutputEncoding = Encoding.Unicode;

            using (var cts = new CancellationTokenSource())
            {
                cts.CancelAfter(WaitInMS);
                using (var result = await p.StartAndWaitForExitAsync(cts.Token))
                {
                    Assert.True(result.Exited);

                    Assert.Equal("a" + Environment.NewLine, result.StandardOutput.ReadToEnd());
                }
            }
        }

        [Fact]
        public void TestManyOutputLines()
        {
            const int ExpectedLineCount = 144;

            int nonWhitespaceLinesReceived = 0;
            int totalLinesReceived = 0;

            Process p = CreateProcessPortable(RemotelyInvokable.Write144Lines);
            p.StartInfo.RedirectStandardOutput = true;
            p.OutputDataReceived += (s, e) =>
            {
                if (!string.IsNullOrWhiteSpace(e.Data))
                {
                    nonWhitespaceLinesReceived++;
                }
                totalLinesReceived++;
            };
            p.Start();
            p.BeginOutputReadLine();

            Assert.True(p.WaitForExit(WaitInMS));
            p.WaitForExit(); // This ensures async event handlers are finished processing.

            Assert.Equal(ExpectedLineCount, nonWhitespaceLinesReceived);
            Assert.Equal(ExpectedLineCount + 1, totalLinesReceived);
        }

        [Fact]
        public async Task TestManyOutputLinesAsync()
        {
            const int ExpectedLineCount = 144;

            int totalLinesReceived = 0;

            Process p = CreateProcessPortable(RemotelyInvokable.Write144Lines);
            p.StartInfo.RedirectStandardOutput = true;

            using (var cts = new CancellationTokenSource())
            {
                cts.CancelAfter(WaitInMS);
                using (var result = await p.StartAndWaitForExitAsync(cts.Token))
                {
                    Assert.True(result.Exited);

                    while (result.StandardOutput.ReadLine() != null)
                    {
                        totalLinesReceived++;
                    }
                }
            }

            Assert.Equal(ExpectedLineCount, totalLinesReceived);
        }

        [Fact]
        public void TestClosingStreamsAsyncDoesNotThrow()
        {
            Process p = CreateProcessPortable(RemotelyInvokable.WriteLinesAfterSignal);
            p.StartInfo.RedirectStandardInput = true;
            p.StartInfo.RedirectStandardOutput = true;
            p.StartInfo.RedirectStandardError = true;

            // On netfx, the handler is called once with the Data as null, even if the process writes nothing to the pipe.
            // That behavior is documented here https://docs.microsoft.com/en-us/dotnet/api/system.diagnostics.datareceivedeventhandler
            var outputHandlerCalled = false;
            var errorHandlerCalled = false;
            p.OutputDataReceived += (s, e) =>
            {
                var callbackAllowed = PlatformDetection.IsFullFramework && e.Data == null && !outputHandlerCalled;
                Assert.True(callbackAllowed, "OutputDataReceived called after closing the process");
                outputHandlerCalled = true;
            };
            p.ErrorDataReceived += (s, e) =>
            {
                var callbackAllowed = PlatformDetection.IsFullFramework && e.Data == null && !errorHandlerCalled;
                Assert.True(callbackAllowed, "ErrorDataReceived called after closing the process");
                errorHandlerCalled = true;
            };

            p.Start();
            p.BeginOutputReadLine();
            p.BeginErrorReadLine();

            p.StandardInput.WriteLine();
            p.Close();
        }

        [Fact]
        public void TestClosingStreamsUndefinedDoesNotThrow()
        {
            Process p = CreateProcessPortable(RemotelyInvokable.WriteLinesAfterSignal);
            p.StartInfo.RedirectStandardInput = true;
            p.StartInfo.RedirectStandardOutput = true;
            p.StartInfo.RedirectStandardError = true;

            p.Start();
            p.StandardInput.WriteLine();
            p.Close();

            // Accessing the streams at this point should throw because they have been closed
            Assert.Throws<InvalidOperationException>(() => p.StandardOutput);
            Assert.Throws<InvalidOperationException>(() => p.StandardError);
        }

        [Fact]
        public void TestClosingSyncModeDoesNotCloseStreams()
        {
            Process p = CreateProcessPortable(RemotelyInvokable.WriteLinesAfterSignal);
            p.StartInfo.RedirectStandardInput = true;
            p.StartInfo.RedirectStandardOutput = true;
            p.StartInfo.RedirectStandardError = true;

            p.Start();

            var output = p.StandardOutput;
            var error = p.StandardError;

            p.StandardInput.WriteLine();
            p.Close();

            Thread.Sleep(100);

            var expectedOutput = $"This is the first line to output{Environment.NewLine}This is the second line to output{Environment.NewLine}";
            var expectedError = $"This is the first line to error{Environment.NewLine}This is the second line to error{Environment.NewLine}";

            Assert.Equal(expectedOutput, output.ReadToEnd());
            Assert.Equal(expectedError, error.ReadToEnd());
        }

        [Fact]
        public async Task TestTimeoutOfStartAndWaitForExitAsyncDoesNotCloseStreams()
        {
            Process p = CreateProcessPortable(RemotelyInvokable.WriteLinesAfterSignal);
            p.StartInfo.RedirectStandardInput = true;
            p.StartInfo.RedirectStandardOutput = true;
            p.StartInfo.RedirectStandardError = true;

            using (var cts = new CancellationTokenSource(0))
            {
                var result = await p.StartAndWaitForExitAsync(cts.Token);
                Assert.False(result.Exited);

                var expectedOutput = $"This is the first line to output{Environment.NewLine}This is the second line to output{Environment.NewLine}";
                var expectedError = $"This is the first line to error{Environment.NewLine}This is the second line to error{Environment.NewLine}";

                //Assert.Equal(expectedOutput, result.StandardOutput.ReadToEnd());
                //Assert.Equal(expectedError, result.StandardError.ReadToEnd());

                p.StandardInput.WriteLine();
                Assert.True(p.WaitForExit(WaitInMS));

                var expectedOutput2 = $"This is the first line to output{Environment.NewLine}This is the second line to output{Environment.NewLine}";
                var expectedError2 = $"This is the first line to error{Environment.NewLine}This is the second line to error{Environment.NewLine}";

                Assert.Equal(expectedOutput2, result.StandardOutput.ReadToEnd());
                Assert.Equal(expectedError2, result.StandardError.ReadToEnd());

                // TODO: MATTKOT: Bit off too much again. StartAndWaitForExit should just do that. If you need to move the output, that's yet another method.
            }
        }

        [Fact]
        public async Task TestStreamNegativeTests()
        {
            {
                Process p = new Process();
                Assert.Throws<InvalidOperationException>(() => p.StandardOutput);
                Assert.Throws<InvalidOperationException>(() => p.StandardError);
                Assert.Throws<InvalidOperationException>(() => p.BeginOutputReadLine());
                Assert.Throws<InvalidOperationException>(() => p.BeginErrorReadLine());
                Assert.Throws<InvalidOperationException>(() => p.CancelOutputRead());
                Assert.Throws<InvalidOperationException>(() => p.CancelErrorRead());
            }

            {
                Process p = CreateProcessPortable(RemotelyInvokable.StreamBody);
                p.StartInfo.RedirectStandardOutput = true;
                p.StartInfo.RedirectStandardError = true;
                p.OutputDataReceived += (s, e) => {};
                p.ErrorDataReceived += (s, e) => {};

                p.Start();
                p.BeginOutputReadLine();
                p.BeginErrorReadLine();

                Assert.Throws<InvalidOperationException>(() => p.StandardOutput);
                Assert.Throws<InvalidOperationException>(() => p.StandardError);
                Assert.True(p.WaitForExit(WaitInMS));
            }

            {
                Process p = CreateProcessPortable(RemotelyInvokable.StreamBody);
                p.StartInfo.RedirectStandardOutput = true;
                p.StartInfo.RedirectStandardError = true;

                using (var cts = new CancellationTokenSource())
                {
                    cts.CancelAfter(WaitInMS);
                    using (var result = await p.StartAndWaitForExitAsync(cts.Token))
                    {
                        Assert.True(result.Exited);
                    }
                }

                Assert.Throws<InvalidOperationException>(() => p.StandardOutput);
                Assert.Throws<InvalidOperationException>(() => p.StandardError);
            }

            {
                Process p = CreateProcessPortable(RemotelyInvokable.StreamBody);
                p.StartInfo.RedirectStandardOutput = true;
                p.StartInfo.RedirectStandardError = true;
                p.OutputDataReceived += (s, e) => {};
                p.ErrorDataReceived += (s, e) => {};

                p.Start();

                StreamReader output = p.StandardOutput;
                StreamReader error = p.StandardError;

                Assert.Throws<InvalidOperationException>(() => p.BeginOutputReadLine());
                Assert.Throws<InvalidOperationException>(() => p.BeginErrorReadLine());
                Assert.True(p.WaitForExit(WaitInMS));
            }
        }
    }
}
