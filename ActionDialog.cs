using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace EdgePad
{
    public sealed class ActionDialog : Window
    {
        public ActionSpec Result { get; private set; }

        private const string IdNone = "__none";
        private const string IdCustom = "__custom";
        private const string IdCommand = "__command";

        private readonly ComboBox _combo = new ComboBox();
        private readonly TextBox _text = new TextBox();
        private readonly Button _record = new Button { Content = "录制" };
        private readonly TextBlock _hint = new TextBlock();

        private bool _recording;

        public ActionDialog(ActionSpec current)
        {
            Title = "设置边缘动作";
            Width = 500;
            Height = 320;
            ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = new SolidColorBrush(Color.FromRgb(0x14, 0x14, 0x18));
            Foreground = new SolidColorBrush(Color.FromRgb(0xE6, 0xE6, 0xEC));

            BuildUi();
            SelectCurrent(current);
        }

        private void BuildUi()
        {
            var root = new DockPanel { Margin = new Thickness(20) };

            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
            var cancel = new Button { Content = "取消", Padding = new Thickness(16, 6, 16, 6), Margin = new Thickness(0, 0, 8, 0) };
            var ok = new Button { Content = "确定", Padding = new Thickness(16, 6, 16, 6) };
            cancel.Click += (s, e) => { DialogResult = false; Close(); };
            ok.Click += (s, e) => Confirm();
            buttons.Children.Add(cancel);
            buttons.Children.Add(ok);
            DockPanel.SetDock(buttons, Dock.Bottom);
            root.Children.Add(buttons);

            var content = new StackPanel();

            content.Children.Add(new TextBlock { Text = "动作", Foreground = new SolidColorBrush(Color.FromRgb(0x9A, 0x9A, 0xA6)), FontSize = 12, Margin = new Thickness(0, 0, 0, 6) });

            _combo.Height = 30;
            _combo.Margin = new Thickness(0, 0, 0, 14);
            _combo.Items.Add(MakeItem("未设置", IdNone));
            foreach (var p in Actions.Presets) _combo.Items.Add(MakeItem(p.Name, p.Id));
            _combo.Items.Add(MakeItem("自定义快捷键…", IdCustom));
            _combo.Items.Add(MakeItem("运行命令…", IdCommand));
            _combo.SelectionChanged += (s, e) => UpdateEnabled();
            content.Children.Add(_combo);

            content.Children.Add(new TextBlock { Text = "按键 / 命令", Foreground = new SolidColorBrush(Color.FromRgb(0x9A, 0x9A, 0xA6)), FontSize = 12, Margin = new Thickness(0, 0, 0, 6) });

            var row = new DockPanel();
            _record.Padding = new Thickness(12, 5, 12, 5);
            _record.Margin = new Thickness(8, 0, 0, 0);
            _record.Click += (s, e) => StartRecording();
            DockPanel.SetDock(_record, Dock.Right);
            _text.Height = 30;
            _text.VerticalContentAlignment = VerticalAlignment.Center;
            _text.Padding = new Thickness(8, 0, 8, 0);
            row.Children.Add(_record);
            row.Children.Add(_text);
            content.Children.Add(row);

            _hint.Text = "选择预设，或点“录制”后按下组合键；选“运行命令”时填写 cmd 命令。";
            _hint.Foreground = new SolidColorBrush(Color.FromRgb(0x70, 0x70, 0x80));
            _hint.FontSize = 11;
            _hint.TextWrapping = TextWrapping.Wrap;
            _hint.Margin = new Thickness(0, 12, 0, 0);
            content.Children.Add(_hint);

            root.Children.Add(content);
            Content = root;

            PreviewKeyDown += OnKeyDownCapture;
        }

        private static ComboBoxItem MakeItem(string text, string tag)
        {
            var item = new ComboBoxItem { Content = text, Tag = tag };
            item.Padding = new Thickness(8, 4, 8, 4);
            return item;
        }

        private void SelectCurrent(ActionSpec current)
        {
            if (current == null || current.Kind == "none") { _combo.SelectedIndex = 0; return; }

            if (current.Kind == "open")
            {
                _combo.SelectedIndex = _combo.Items.Count - 1;
                _text.Text = current.Value ?? "";
                return;
            }

            foreach (ComboBoxItem item in _combo.Items)
            {
                string tag = item.Tag as string;
                if (tag == null || tag.StartsWith("__")) continue;
                var p = Actions.Find(tag);
                if (p != null && p.Value == current.Value && p.Kind == current.Kind) { item.IsSelected = true; return; }
            }

            foreach (ComboBoxItem item in _combo.Items)
            {
                if ((item.Tag as string) == IdCustom) { item.IsSelected = true; break; }
            }
            _text.Text = current.Value ?? "";
        }

        private void UpdateEnabled()
        {
            string tag = SelectedTag();
            bool editable = tag == IdCustom || tag == IdCommand;
            _text.IsEnabled = editable;
            _record.IsEnabled = tag == IdCustom;
        }

        private string SelectedTag()
        {
            var item = _combo.SelectedItem as ComboBoxItem;
            return item == null ? IdNone : (item.Tag as string);
        }

        private void StartRecording()
        {
            _recording = true;
            _hint.Text = "正在录制……按下组合键（Esc 取消）";
            Focus();
            Keyboard.Focus(this);
        }

        private void OnKeyDownCapture(object sender, KeyEventArgs e)
        {
            if (!_recording) return;
            e.Handled = true;

            Key key = e.Key == Key.System ? e.SystemKey : e.Key;
            if (key == Key.Escape) { _recording = false; _hint.Text = "已取消录制"; return; }
            if (key == Key.LeftCtrl || key == Key.RightCtrl || key == Key.LeftShift || key == Key.RightShift ||
                key == Key.LeftAlt || key == Key.RightAlt || key == Key.LWin || key == Key.RWin)
                return;

            string name = KeySender.KeyName(key);
            if (name == null) { _hint.Text = "不支持的按键：" + key; return; }

            var parts = new List<string>();
            var mods = Keyboard.Modifiers;
            if ((mods & ModifierKeys.Control) != 0) parts.Add("Ctrl");
            if ((mods & ModifierKeys.Shift) != 0) parts.Add("Shift");
            if ((mods & ModifierKeys.Alt) != 0) parts.Add("Alt");
            if ((mods & ModifierKeys.Windows) != 0) parts.Add("Win");
            parts.Add(name);

            _text.Text = string.Join("+", parts);
            _recording = false;
            _hint.Text = "已录制：" + _text.Text;
        }

        private void Confirm()
        {
            string tag = SelectedTag();
            string value = (_text.Text ?? "").Trim();

            if (tag == IdNone) Result = Actions.None();
            else if (tag == IdCommand)
            {
                if (value.Length == 0) { _hint.Text = "请填写要运行的命令"; return; }
                Result = new ActionSpec { Kind = "open", Value = value, Display = "运行 " + value };
            }
            else if (tag == IdCustom)
            {
                if (value.Length == 0) { _hint.Text = "请录制或填写按键组合"; return; }
                Result = new ActionSpec { Kind = "keys", Value = value, Display = value };
            }
            else Result = Actions.FromPreset(tag);

            DialogResult = true;
            Close();
        }
    }
}
