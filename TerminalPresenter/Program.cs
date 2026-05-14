using System.Diagnostics;
using System.IO.Pipes;
using Spectre.Console;
using System.Text.RegularExpressions;
using System.IO;

namespace TerminalPresenter;

class Program
{
    static async Task Main(string[] args)
    {
        // Om appen startas i "Viewer-läge" (det externa fönstret)
        if (args.Length > 0 && args[0] == "viewer")
        {
            await RunViewerAsync();
            return;
        }

        // Annars är vi i "Controller-läge" (din laptop)
        string markdownFilePath = "./slides/example-presentation.md"; // Standardfil

        // Om användaren skickar med ett filnamn: dotnet run min_presentation.md
        if (args.Length > 0)
        {
             markdownFilePath = args[0];
        }

        if (!File.Exists(markdownFilePath))
        {
            AnsiConsole.MarkupLine($"[bold red]Fel:[/] Hittade inte filen '{markdownFilePath}'.");
            return;
        }

        await RunControllerAsync(markdownFilePath);
    }

    static async Task RunControllerAsync(string filePath)
    {
        AnsiConsole.MarkupLine($"[bold green]Laddar presentation från:[/] {filePath}...");

        // Läs in slides och separera dem baserat på "---"
        var markdownText = await File.ReadAllTextAsync(filePath);
        var slides = markdownText.Split("---", StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

        if (slides.Length == 0)
        {
             AnsiConsole.MarkupLine("[bold red]Filen verkar vara tom eller felformaterad.[/]");
             return;
        }

        // Starta viewer-processen i ett nytt fönster
        using var process = new Process();
        process.StartInfo.FileName = "dotnet";
        // Anropa sig själv med "viewer" som argument
        process.StartInfo.Arguments = "run viewer";
        process.StartInfo.UseShellExecute = true;
        process.StartInfo.CreateNoWindow = false;
        process.Start();

        // Starta Named Pipe Server
        await using var pipeServer = new NamedPipeServerStream("TerminalPresenterPipe", PipeDirection.Out);
        AnsiConsole.MarkupLine("[yellow]Väntar på att presentationsfönstret ska ansluta...[/]");
        await pipeServer.WaitForConnectionAsync();

        using var writer = new StreamWriter(pipeServer) { AutoFlush = true };

        int currentSlideIndex = 0;

        while (true)
        {
            Console.Clear();
            var currentSlide = slides[currentSlideIndex];

            // Extrahera och ta bort notes från presentationen som ska visas
            var (slideContent, notes) = ExtractNotes(currentSlide);

            // Skicka sliden till Viewern (kodar till Base64 för säker transport över pipen)
            await writer.WriteLineAsync(Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(slideContent)));

            // Rendera Speaker Notes lokalt
            AnsiConsole.Write(new Rule($"[bold blue]Slide {currentSlideIndex + 1} av {slides.Length}[/]").RuleStyle("blue"));
            AnsiConsole.MarkupLine("\n[bold yellow]🎙️ SPEAKER NOTES:[/]\n");
            AnsiConsole.MarkupLine($"[italic]{(string.IsNullOrWhiteSpace(notes) ? "Inga anteckningar för denna slide." : notes)}[/]\n");

            AnsiConsole.Write(new Rule("[grey]Kontroller[/]").RuleStyle("grey"));
            AnsiConsole.MarkupLine("[grey]Tryck på [bold white]HÖGERPIL[/] för nästa, [bold white]VÄNSTERPIL[/] för föregående, [bold red]ESC[/] för att avsluta.[/]");

            var key = Console.ReadKey(true).Key;

            if (key == ConsoleKey.RightArrow && currentSlideIndex < slides.Length - 1)
                currentSlideIndex++;
            else if (key == ConsoleKey.LeftArrow && currentSlideIndex > 0)
                currentSlideIndex--;
            else if (key == ConsoleKey.Escape)
                break;
        }

        // Signalera till viewern att stänga ner
        await writer.WriteLineAsync("EXIT");
    }

    static async Task RunViewerAsync()
    {
        Console.Title = "Terminal Presenter - Live";

        await using var pipeClient = new NamedPipeClientStream(".", "TerminalPresenterPipe", PipeDirection.In);
        await pipeClient.ConnectAsync();

        using var reader = new StreamReader(pipeClient);

        while (true)
        {
            var message = await reader.ReadLineAsync();
            if (message == "EXIT" || message == null) break;

            var slideContent = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(message));

            Console.Clear();

            AnsiConsole.Write(
                new FigletText("Live Presentation")
                    .Centered()
                    .Color(Color.Fuchsia));

            AnsiConsole.Write(new Rule().RuleStyle("fuchsia"));
            AnsiConsole.WriteLine();

            // Rendera markdown med Spectre.Console
            var markdown = new Markup(slideContent);
            AnsiConsole.Write(markdown);

            Console.CursorVisible = false;
        }
    }

    static (string slideContent, string notes) ExtractNotes(string markdownString)
    {
        // Regex för att hitta <!-- NOTES: ... --> oavsett radbrytningar
        var notesRegex = new Regex(@"<!--\s*NOTES:\s*(.*?)\s*-->", RegexOptions.Singleline);
        var match = notesRegex.Match(markdownString);

        string notes = match.Success ? match.Groups[1].Value.Trim() : string.Empty;
        string slideContent = notesRegex.Replace(markdownString, string.Empty).Trim();

        return (slideContent, notes);
    }
}