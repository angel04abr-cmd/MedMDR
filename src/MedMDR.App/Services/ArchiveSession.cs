using System.Windows;
using MedMDR.Core;

namespace MedMDR.Wpf;

/// <summary>Un archivo abierto junto con su contraseña (se pide una sola vez y se recuerda mientras esté abierto).</summary>
internal sealed class ArchiveSession : IDisposable
{
    public string Path { get; }
    public string? Password { get; private set; }
    public IArchive Archive { get; private set; }

    private ArchiveSession(string path, IArchive archive, string? pw) { Path = path; Archive = archive; Password = pw; }

    public static ArchiveSession Wrap(string path, IArchive archive, string? pw) => new(path, archive, pw);

    public static ArchiveSession? Open(Window? owner, string path)
    {
        string? pw = null;
        bool wrong = false;
        for (;;)
        {
            try { return new ArchiveSession(path, ArchiveOpener.Open(path, pw), pw); }
            catch (PasswordRequiredException) { }
            catch (WrongPasswordException) { wrong = true; }
            catch (Exception ex) when (ex is MedMDRException or IOException or UnauthorizedAccessException)
            {
                Msg.Error(owner, $"No se puede abrir «{System.IO.Path.GetFileName(path)}».\n\n{Msg.Describe(ex)}", "Abrir archivo");
                return null;
            }
            pw = AskPassword(owner, path, wrong);
            if (pw == null) return null;
        }
    }

    private static string? AskPassword(Window? owner, string path, bool wrong) =>
        TextPromptWindow.AskPassword(owner, (wrong ? "Contraseña incorrecta. " : "") + $"Escribe la contraseña de «{System.IO.Path.GetFileName(path)}»:");

    /// <summary>
    /// Si el contenido está cifrado pero el listado se ve (ZIP con contraseña, .mdr sin nombres cifrados, RAR solo con contenido cifrado),
    /// ofrece escribir la contraseña ya, para poder modificar y extraer sin más preguntas. Cancelar solo deja ver la lista.
    /// </summary>
    public void PromptIfEncrypted(Window? owner)
    {
        if (!Archive.IsEncrypted || !string.IsNullOrEmpty(Password)) return;
        string name = System.IO.Path.GetFileName(Path);
        for (;;)
        {
            string? pw = TextPromptWindow.AskPassword(owner,
                $"El contenido de «{name}» está cifrado.\nEscribe la contraseña para poder extraerlo o modificarlo (puedes cancelar y ver solo la lista):");
            if (pw == null) return;
            try
            {
                var fresh = ArchiveOpener.Open(Path, pw);
                // comprobar de verdad que la clave sirve (algunos formatos solo lo notan al descifrar)
                var first = fresh.Entries.Where(e => !e.IsDirectory && e.Size > 0).OrderBy(e => e.Size).FirstOrDefault();
                if (first != null)
                {
                    try { fresh.ReadAll(first, 64); }
                    catch (WrongPasswordException) { fresh.Dispose(); Msg.Warn(owner, "Contraseña incorrecta."); continue; }
                }
                Archive.Dispose();
                Archive = fresh;
                Password = pw;
                return;
            }
            catch (WrongPasswordException) { Msg.Warn(owner, "Contraseña incorrecta."); }
            catch (Exception ex) { Msg.Error(owner, Msg.Describe(ex)); return; }
        }
    }

    /// <summary>Pide la contraseña y reabre el archivo con ella. Devuelve false si se cancela.</summary>
    public bool Unlock(Window? owner, bool wrong)
    {
        string? pw = AskPassword(owner, Path, wrong);
        if (pw == null) return false;
        try
        {
            var fresh = ArchiveOpener.Open(Path, pw);
            Archive.Dispose();
            Archive = fresh;
            Password = pw;
            return true;
        }
        catch (WrongPasswordException) { return Unlock(owner, true); }
        catch (Exception ex) { Msg.Error(owner, Msg.Describe(ex)); return false; }
    }

    public void Dispose() => Archive.Dispose();
}
