using System.Drawing;
using System.Windows.Forms;

namespace GRBLMachine.UI
{
  internal static class UiTheme
  {
    internal static readonly Color Panel = Color.FromArgb(244, 247, 249);
    internal static readonly Color Ink = Color.FromArgb(39, 55, 69);
    internal static readonly Color Header = Color.FromArgb(57, 76, 94);
    internal static readonly Color Border = Color.FromArgb(173, 187, 198);
    internal static readonly Color Input = Color.White;

    internal static void Apply(Expander expander)
    {
      expander.BackColor = Header;
      expander.ContentPanel.BackColor = Panel;
      expander.HeaderLabel.BackColor = Header;
      expander.HeaderLabel.ForeColor = Color.White;
      expander.HeaderLabel.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
      ApplyChildren(expander.ContentPanel);
    }

    private static void ApplyChildren(Control parent)
    {
      foreach (Control control in parent.Controls)
      {
        if (control is Button)
        {
          Button button = (Button)control;
          button.FlatStyle = FlatStyle.Flat;
          button.FlatAppearance.BorderSize = 1;
          button.FlatAppearance.BorderColor = Border;
          button.FlatAppearance.MouseOverBackColor = Color.FromArgb(224, 237, 247);
          button.FlatAppearance.MouseDownBackColor = Color.FromArgb(204, 225, 240);
          button.BackColor = Color.FromArgb(232, 240, 246);
          button.ForeColor = Ink;
          button.UseVisualStyleBackColor = false;
          button.Cursor = Cursors.Hand;
          if (button.Font == null || button.Font.Name != "Segoe UI")
            button.Font = new Font("Segoe UI", 8.5F, FontStyle.Bold);
        }
        else if (control is TextBoxBase)
        {
          TextBoxBase input = (TextBoxBase)control;
          input.BackColor = Input;
          input.ForeColor = Ink;
          input.BorderStyle = BorderStyle.FixedSingle;
        }
        else if (control is ComboBox)
        {
          ComboBox input = (ComboBox)control;
          input.BackColor = Input;
          input.ForeColor = Ink;
          input.FlatStyle = FlatStyle.Flat;
        }
        else if (control is ListBox)
        {
          control.BackColor = Input;
          control.ForeColor = Ink;
        }
        else if (control is Label)
          control.ForeColor = Ink;
        else if (control is CheckBox || control is RadioButton)
          control.ForeColor = Ink;

        if (control.HasChildren)
          ApplyChildren(control);
      }
    }
  }
}
