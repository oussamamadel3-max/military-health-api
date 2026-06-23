namespace Application.Interfaces.Services;

public interface ITextInputSanitizer
{
    string? SanitizePlainText(string? input);
}
