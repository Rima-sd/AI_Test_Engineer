namespace TestEngineerWorker.Utils;

public static class ConsoleProgress
{
    public static void Show(string message)
    {
        Console.WriteLine(
            $"[{DateTime.Now:HH:mm:ss}] {message}");
    }

    public static void ShowSuccess(string message)
    {
        Console.ForegroundColor = ConsoleColor.Green;
        Show($"SUCCESS: {message}");
        Console.ResetColor();
    }

    public static void ShowError(string message)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Show($"ERROR: {message}");
        Console.ResetColor();
    }
}