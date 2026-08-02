using System.ComponentModel;
using System.Diagnostics;
using System.IO.Pipes;

namespace JustyBase.Public.Lib.Services;

public sealed class PipeCommunicationService(string jbMessagePipe)
{
    private const int ErrorPipeBusy = 231;
    private const int TransientRetryDelayMs = 250;
    private const int MaxTransientRetriesBeforeReport = 20;

    public Action<string>? ActivateOpenedFileAction { get; init; }
    public Action? RestoreAction { get; init; }
    public required Action<Exception> ExceptionAction { get; init; }

    private readonly string _jb_message_pipe = jbMessagePipe;

    public void Start()
    {
        Task waitForExternalMessagesTask = new(() => WaitForFileToOpenFromSystem(), TaskCreationOptions.LongRunning);
        waitForExternalMessagesTask.Start();
    }

    private void WaitForFileToOpenFromSystem()
    {
        int consecutiveTransientFailures = 0;

        while (true)
        {
            try
            {
                // maxNumberOfServerInstances: 1 — only the primary UI instance should own this pipe.
                using NamedPipeServerStream pipeServer = new(
                    _jb_message_pipe,
                    PipeDirection.InOut,
                    maxNumberOfServerInstances: 1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.None);
                consecutiveTransientFailures = 0;
                Debug.WriteLine("NamedPipeServerStream object created.");
                Debug.Write("Waiting for client connection...");
                pipeServer.WaitForConnection();
                Debug.WriteLine("Client connected.");

                try
                {
                    using StreamReader sr = new(pipeServer);
                    while (!sr.EndOfStream)
                    {
                        string? line = sr.ReadLine();
                        Debug.WriteLine(line);
                        if (line is not null && File.Exists(line))
                        {
                            ActivateOpenedFileAction?.Invoke(line);
                        }
                        else if (line == "RESTORE")
                        {
                            RestoreAction?.Invoke();
                        }
                    }
                }
                // Catch the IOException that is raised if the pipe is broken
                // or disconnected.
                catch (IOException e)
                {
                    Debug.WriteLine("ERROR: {0}", e.Message);
                }

                // Brief pause before recreating so Windows can release the previous instance.
                // Without this, the next Create() often hits ERROR_PIPE_BUSY.
                Thread.Sleep(TransientRetryDelayMs);
            }
            catch (Exception ex) when (IsTransientPipeBusy(ex))
            {
                consecutiveTransientFailures++;
                Debug.WriteLine("Transient pipe busy ({0}): {1}", consecutiveTransientFailures, ex.Message);
                Thread.Sleep(TransientRetryDelayMs);

                // Avoid spamming the user while another instance still owns the pipe,
                // or while the previous server handle is still releasing.
                if (consecutiveTransientFailures == MaxTransientRetriesBeforeReport)
                {
                    ExceptionAction?.Invoke(ex);
                }
            }
            catch (Exception ex)
            {
                consecutiveTransientFailures = 0;
                ExceptionAction?.Invoke(ex);
                Thread.Sleep(TransientRetryDelayMs);
            }
        }
    }

    private static bool IsTransientPipeBusy(Exception ex)
    {
        for (Exception? current = ex; current is not null; current = current.InnerException)
        {
            if (current is Win32Exception win32 && win32.NativeErrorCode == ErrorPipeBusy)
            {
                return true;
            }

            if (current is IOException && (current.HResult & 0xFFFF) == ErrorPipeBusy)
            {
                return true;
            }
        }

        // Localized message fallback (e.g. PL: "Wszystkie wystąpienia potoku są zajęte.")
        return ex.Message.Contains("pipe", StringComparison.OrdinalIgnoreCase)
               && (ex.Message.Contains("busy", StringComparison.OrdinalIgnoreCase)
                   || ex.Message.Contains("zajęte", StringComparison.OrdinalIgnoreCase)
                   || ex.Message.Contains("zajete", StringComparison.OrdinalIgnoreCase));
    }
}
