namespace Listly.Views;

/// <summary>
/// A single M2_Commander pane (header path + close button, column headers, and the file list).
/// The window builds two-to-four of these at runtime; its <c>Pane</c> wrapper reads the named
/// parts (<see cref="List"/>, <see cref="PathText"/>, <see cref="Root"/>, <see cref="HeaderBar"/>,
/// <see cref="CloseButton"/>) so the rest of the commander logic is unchanged.
/// </summary>
public partial class CommanderPane : System.Windows.Controls.UserControl
{
    public CommanderPane()
    {
        InitializeComponent();
    }
}
