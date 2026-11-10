namespace Asa.App.Services;

public sealed class GameModeDetector
{
    private const int StateBusy = 2;
    private const int StateFullScreenGame = 3;
    private const int StatePresentation = 4;

    public bool Enabled { get; set; } = true;

    public bool IsUserBusy()
    {
        if (!Enabled)
        {
            return false;
        }

        try
        {
            if (NativeMethods.SHQueryUserNotificationState(out int state) != 0)
            {
                return false;
            }

            return state is StateBusy or StateFullScreenGame or StatePresentation;
        }
        catch (EntryPointNotFoundException)
        {
            return false;
        }
    }
}
