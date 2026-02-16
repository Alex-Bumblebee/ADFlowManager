namespace ADFlowManager.UI.Views.Dialogs;

/// <summary>
/// Dialog de saisie simple (remplace VB InputBox pour compatibilité WPF).
/// </summary>
public partial class InputDialog : System.Windows.Window
{
    public string ResultText { get; private set; } = "";

    public InputDialog(string title, string prompt, string placeholder = "")
    {
        InitializeComponent();

        Title = title;
        PromptText.Text = prompt;
        InputTextBox.PlaceholderText = placeholder;

        Loaded += (_, _) => InputTextBox.Focus();
    }

    private void OkButton_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        ResultText = InputTextBox.Text;
        DialogResult = true;
        Close();
    }

    private void CancelButton_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void InputTextBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.Enter)
        {
            OkButton_Click(sender, e);
        }
        else if (e.Key == System.Windows.Input.Key.Escape)
        {
            CancelButton_Click(sender, e);
        }
    }
}
