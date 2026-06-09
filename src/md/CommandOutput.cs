using CliWrap.Buffered;

namespace Devlooped;

public static class CommandOutput
{
    public static void Finish(bool wroteMarkdown, int exitCode, BufferedCommandResult? result, Action writeFallback)
    {
        if (wroteMarkdown)
            return;

        if (exitCode != 0)
        {
            writeFallback();
            return;
        }

        if (result is not null)
            DotnetRunner.ReplayCapturedOutput(result);
    }
}