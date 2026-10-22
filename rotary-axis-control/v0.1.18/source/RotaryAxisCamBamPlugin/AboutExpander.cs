namespace GRBLMachine.UI
{
  public partial class AboutExpander : Expander
  {
    public AboutExpander()
    {
      InitializeComponent();
      UiTheme.Apply(this);

      VersionLabel.Text = "v" + GRBLMachinePlugin.Version;
    }
  }
}
