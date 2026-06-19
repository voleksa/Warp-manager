namespace WarpManager.Models;

public record WarpResult(bool Success, string Output, string Error = "")
{
    public static WarpResult Ok(string output)  => new(true, output);
    public static WarpResult Fail(string error) => new(false, string.Empty, error);
}
