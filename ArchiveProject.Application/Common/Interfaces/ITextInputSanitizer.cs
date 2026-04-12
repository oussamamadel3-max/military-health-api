namespace ArchiveProject.Application.Common.Interfaces;

public interface ITextInputSanitizer
{
    string? SanitizePlainText(string? input);
}
