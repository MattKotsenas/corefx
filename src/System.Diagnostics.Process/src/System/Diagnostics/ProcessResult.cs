using System.IO;

namespace System.Diagnostics
{
    public class ProcessResult : IDisposable
    {
        // TODO: MATTKOT: ProcessResult implements IDisposable because it takes a Stream, but that should always be a MemoryStream, so do we need it?
        internal ProcessResult(bool exited, int? exitCode, Stream standardOutput, Stream standardError)
        {
            Exited = exited;
            ExitCode = exitCode;
            StandardOutput = new StreamReader(standardOutput);
            StandardError = new StreamReader(standardError);
        }

        public bool Exited { get; }
        public int? ExitCode { get; }
        public StreamReader StandardOutput { get; }
        public StreamReader StandardError { get; }


        public void Dispose()
        {
            Dispose(true);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                StandardOutput?.Dispose();
                StandardError?.Dispose();
            }
        }
    }
}
