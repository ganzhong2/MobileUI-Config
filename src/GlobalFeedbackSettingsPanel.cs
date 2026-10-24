using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace MobileUiConfig
{
    // 全局触控反馈卡片：视觉 / 音效 / 震动三套全局配置。按键自身未开启对应反馈时，
    // 真机回落使用本套配置；按键自身配置优先级高于本套。震动子面板复用全局 XInput 配置。
    public class GlobalFeedbackSettingsPanel : SectionPanel
    {
        private GlobalFeedbackConfig _global;
        private RumbleSettingsPanel _rumbleChild;
        private bool _binding;

        // 视觉反馈
        private ToggleSwitch _visualToggle;
        private ModernSelect _visualSelect;
        private Panel _visualColorSwatch;
        private Label _visualColorNameLabel;
        private StepperBox _visualIntensityStep;
        private StepperBox _visualDurationStep;
        private StepperBox _visualScaleStep;

        // 音效反馈
        private ToggleSwitch _soundToggle;
        private ModernSelect _soundSelect;
        private StepperBox _soundVolumeStep;
        private ModernButton _chooseSound;
        private ModernButton _testSound;
        private Label _soundFileLabel;

        public event EventHandler Changed;

        public GlobalFeedbackSettingsPanel()
        {
            BackColor = Theme.SurfaceAlt;
            Height = Theme.S(862);
            Title = "全局触控反馈";
            Hint = "按键未单独开启时，所有按键回落使用本套视觉 / 音效 / 震动；按键自定义优先";

            BuildVisualSection();
            BuildSoundSection();

            _rumbleChild = new RumbleSettingsPanel();
            _rumbleChild.Changed += delegate { RaiseChanged(); };
            Controls.Add(_rumbleChild);

            Resize += delegate { LayoutControls(); };
        }

        private void BuildVisualSection()
        {
            Label visualTitle = MakeTitle("视觉反馈");
            visualTitle.Name = "visualTitle";
            Controls.Add(visualTitle);

            _visualToggle = new ToggleSwitch();
            _visualToggle.CheckedChanged += delegate
            {
                if (_binding || _global == null) return;
                _global.VisualEnabled = _visualToggle.Checked;
                UpdateEnabledState();
                RaiseChanged();
            };
            Controls.Add(_visualToggle);

            _visualSelect = new ModernSelect();
            _visualSelect.SelectionChanged += delegate
            {
                if (_binding || _global == null) return;
                int id;
                if (int.TryParse(_visualSelect.SelectedId, out id)) _global.VisualPreset = id;
                UpdateEnabledState();
                RaiseChanged();
            };
            Controls.Add(_visualSelect);

            _visualColorSwatch = new Panel();
            _visualColorSwatch.Cursor = Cursors.Hand;
            _visualColorSwatch.Click += delegate { ChooseVisualColor(); };
            Controls.Add(_visualColorSwatch);

            _visualColorNameLabel = MakeLabel("白色");
            _visualColorNameLabel.Name = "visualColorNameLabel";
            Controls.Add(_visualColorNameLabel);

            Label intensityLabel = MakeLabel("强度");
            intensityLabel.Name = "intensityLabel";
            Controls.Add(intensityLabel);

            _visualIntensityStep = new StepperBox();
            _visualIntensityStep.Minimum = 0;
            _visualIntensityStep.Maximum = 100;
            _visualIntensityStep.Step = 1;
            _visualIntensityStep.ValueChanged += delegate
            {
                if (_binding || _global == null) return;
                _global.VisualIntensity = _visualIntensityStep.Value;
                RaiseChanged();
            };
            Controls.Add(_visualIntensityStep);

            Label intensityUnit = MakeUnit("%");
            intensityUnit.Name = "intensityUnit";
            Controls.Add(intensityUnit);

            Label durationLabel = MakeLabel("时长");
            durationLabel.Name = "durationLabel";
            Controls.Add(durationLabel);

            _visualDurationStep = new StepperBox();
            _visualDurationStep.Minimum = 100;
            _visualDurationStep.Maximum = 1000;
            _visualDurationStep.Step = 10;
            _visualDurationStep.ValueChanged += delegate
            {
                if (_binding || _global == null) return;
                _global.VisualDurationMs = _visualDurationStep.Value;
                RaiseChanged();
            };
            Controls.Add(_visualDurationStep);

            Label durationUnit = MakeUnit("ms");
            durationUnit.Name = "durationUnit";
            Controls.Add(durationUnit);

            Label scaleLabel = MakeLabel("缩放");
            scaleLabel.Name = "scaleLabel";
            Controls.Add(scaleLabel);

            _visualScaleStep = new StepperBox();
            _visualScaleStep.Minimum = 80;
            _visualScaleStep.Maximum = 100;
            _visualScaleStep.Step = 1;
            _visualScaleStep.ValueChanged += delegate
            {
                if (_binding || _global == null) return;
                _global.VisualScalePercent = _visualScaleStep.Value;
                RaiseChanged();
            };
            Controls.Add(_visualScaleStep);

            Label scaleUnit = MakeUnit("%");
            scaleUnit.Name = "scaleUnit";
            Controls.Add(scaleUnit);
        }

        private void BuildSoundSection()
        {
            Label soundTitle = MakeTitle("音效反馈");
            soundTitle.Name = "soundTitle";
            Controls.Add(soundTitle);

            _soundToggle = new ToggleSwitch();
            _soundToggle.CheckedChanged += delegate
            {
                if (_binding || _global == null) return;
                _global.SoundEnabled = _soundToggle.Checked;
                UpdateEnabledState();
                RaiseChanged();
            };
            Controls.Add(_soundToggle);

            _soundSelect = new ModernSelect();
            _soundSelect.SelectionChanged += delegate
            {
                if (_binding || _global == null) return;
                int id;
                if (int.TryParse(_soundSelect.SelectedId, out id)) _global.SoundPreset = id;
                UpdateEnabledState();
                RaiseChanged();
            };
            Controls.Add(_soundSelect);

            Label volumeLabel = MakeLabel("音量");
            volumeLabel.Name = "volumeLabel";
            Controls.Add(volumeLabel);

            _soundVolumeStep = new StepperBox();
            _soundVolumeStep.Minimum = 0;
            _soundVolumeStep.Maximum = 100;
            _soundVolumeStep.Step = 1;
            _soundVolumeStep.ValueChanged += delegate
            {
                if (_binding || _global == null) return;
                _global.SoundVolume = _soundVolumeStep.Value;
                RaiseChanged();
            };
            Controls.Add(_soundVolumeStep);

            Label volumeUnit = MakeUnit("%");
            volumeUnit.Name = "volumeUnit";
            Controls.Add(volumeUnit);

            _chooseSound = new ModernButton();
            _chooseSound.Text = "选择音效";
            _chooseSound.Style = ModernButtonStyle.Secondary;
            _chooseSound.Height = 34;
            _chooseSound.Click += delegate { ChooseSoundFile(); };
            Controls.Add(_chooseSound);

            _testSound = new ModernButton();
            _testSound.Text = "试听";
            _testSound.Style = ModernButtonStyle.Ghost;
            _testSound.Height = 34;
            _testSound.Click += delegate { TestSound(); };
            Controls.Add(_testSound);

            _soundFileLabel = MakeLabel("未选择自定义音效文件");
            _soundFileLabel.Name = "soundFileLabel";
            Controls.Add(_soundFileLabel);
        }

        public void SetConfig(AppConfig config)
        {
            if (config == null) return;
            _global = config.GlobalFeedback;
            _rumbleChild.SetConfig(config.Rumble);
            _binding = true;
            try
            {
                RefreshOptions();
                _visualToggle.SetCheckedSilent(_global.VisualEnabled);
                _visualIntensityStep.SetValueSilent(_global.VisualIntensity);
                _visualDurationStep.SetValueSilent(_global.VisualDurationMs);
                _visualScaleStep.SetValueSilent(_global.VisualScalePercent);
                SyncColorSwatch();
                _soundToggle.SetCheckedSilent(_global.SoundEnabled);
                _soundVolumeStep.SetValueSilent(_global.SoundVolume);
                SyncSoundFileLabel();
            }
            finally
            {
                _binding = false;
            }
            UpdateEnabledState();
            LayoutControls();
        }

        private void RefreshOptions()
        {
            List<SelectOption> visual = new List<SelectOption>();
            visual.Add(new SelectOption("0", "按下缩放"));
            visual.Add(new SelectOption("1", "高亮闪白"));
            visual.Add(new SelectOption("2", "边框发光"));
            visual.Add(new SelectOption("3", "颜色脉冲"));
            visual.Add(new SelectOption("4", "不透明度脉冲"));
            _visualSelect.SetItems(visual, _global.VisualPreset.ToString(System.Globalization.CultureInfo.InvariantCulture));

            List<SelectOption> sound = new List<SelectOption>();
            sound.Add(new SelectOption("0", "轻咔哒"));
            sound.Add(new SelectOption("1", "确认音"));
            sound.Add(new SelectOption("2", "错误音"));
            sound.Add(new SelectOption("3", "长音"));
            sound.Add(new SelectOption("4", "自定义文件"));
            _soundSelect.SetItems(sound, _global.SoundPreset.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        private void UpdateEnabledState()
        {
            bool vOn = _global != null && _global.VisualEnabled;
            bool sOn = _global != null && _global.SoundEnabled;

            _visualSelect.Enabled = vOn;
            _visualColorSwatch.Enabled = vOn;
            _visualIntensityStep.Enabled = vOn;
            _visualDurationStep.Enabled = vOn;
            bool scaleOn = vOn && _global != null && _global.VisualPreset == 0;
            _visualScaleStep.Enabled = scaleOn;

            _soundSelect.Enabled = sOn;
            _soundVolumeStep.Enabled = sOn;
            bool customSound = sOn && _global != null && _global.SoundPreset == 4;
            _chooseSound.Enabled = customSound;
            _testSound.Enabled = sOn;
        }

        private void ChooseVisualColor()
        {
            if (_global == null) return;
            using (ColorDialog dlg = new ColorDialog())
            {
                dlg.Color = ParseFeedbackColor(_global.VisualColor);
                dlg.FullOpen = true;
                dlg.AnyColor = true;
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                Color c = dlg.Color;
                _global.VisualColor = "#" + c.R.ToString("X2") + c.G.ToString("X2") + c.B.ToString("X2");
                SyncColorSwatch();
                RaiseChanged();
            }
        }

        private void SyncColorSwatch()
        {
            if (_global == null) return;
            _visualColorSwatch.BackColor = ParseFeedbackColor(_global.VisualColor);
            _visualColorNameLabel.Text = _global.VisualColor.ToUpperInvariant();
        }

        private void ChooseSoundFile()
        {
            if (_global == null) return;
            using (OpenFileDialog dlg = new OpenFileDialog())
            {
                dlg.Title = "选择全局音效（WAV / MP3）";
                dlg.Filter = "音频文件 (*.wav;*.mp3;*.wma;*.m4a;*.ogg)|*.wav;*.mp3;*.wma;*.m4a;*.ogg";
                dlg.Multiselect = false;
                dlg.CheckFileExists = true;
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                string imported = ConfigStore.ImportSound(dlg.FileName);
                if (imported.Length == 0) return;
                ConfigStore.DeleteSound(_global.SoundFile);
                _global.SoundFile = imported;
                SyncSoundFileLabel();
                RaiseChanged();
            }
        }

        private void SyncSoundFileLabel()
        {
            if (_global == null) return;
            if (string.IsNullOrEmpty(_global.SoundFile))
            {
                _soundFileLabel.Text = "未选择自定义音效文件";
            }
            else
            {
                string name = _global.SoundFile;
                if (name.Length > 30) name = name.Substring(0, 28) + "…";
                _soundFileLabel.Text = name;
            }
        }

        private void TestSound()
        {
            if (_global == null) return;
            VirtualKey temp = new VirtualKey();
            temp.SoundEnabled = true;
            temp.SoundPreset = _global.SoundPreset;
            temp.SoundFile = _global.SoundFile;
            temp.SoundVolume = _global.SoundVolume;
            FeedbackSound.Play(temp);
        }

        private void LayoutControls()
        {
            if (_visualSelect == null) return;
            float s = Theme.Scale;
            int x = (int)(16 * s);
            int w = Math.Max(80, Width - (int)(32 * s));

            // 视觉反馈分区
            Controls["visualTitle"].SetBounds(x, (int)(44 * s), w - (int)(60 * s), (int)(20 * s));
            _visualToggle.SetBounds(Width - (int)(62 * s), (int)(42 * s), (int)(46 * s), (int)(26 * s));
            _visualSelect.SetBounds(x, (int)(70 * s), w, (int)(38 * s));
            _visualColorSwatch.SetBounds(x, (int)(118 * s), (int)(28 * s), (int)(28 * s));
            _visualColorNameLabel.SetBounds((int)(52 * s), (int)(120 * s), (int)(64 * s), (int)(24 * s));
            Controls["intensityLabel"].SetBounds((int)(124 * s), (int)(120 * s), (int)(52 * s), (int)(24 * s));
            _visualIntensityStep.SetBounds((int)(180 * s), (int)(118 * s), (int)(108 * s), (int)(34 * s));
            Controls["intensityUnit"].SetBounds((int)(294 * s), (int)(120 * s), (int)(24 * s), (int)(24 * s));
            Controls["durationLabel"].SetBounds(x, (int)(158 * s), (int)(52 * s), (int)(24 * s));
            _visualDurationStep.SetBounds((int)(72 * s), (int)(156 * s), (int)(108 * s), (int)(34 * s));
            Controls["durationUnit"].SetBounds((int)(186 * s), (int)(158 * s), (int)(28 * s), (int)(24 * s));
            Controls["scaleLabel"].SetBounds((int)(226 * s), (int)(158 * s), (int)(52 * s), (int)(24 * s));
            _visualScaleStep.SetBounds((int)(282 * s), (int)(156 * s), (int)(108 * s), (int)(34 * s));
            Controls["scaleUnit"].SetBounds((int)(396 * s), (int)(158 * s), (int)(24 * s), (int)(24 * s));

            // 音效反馈分区
            Controls["soundTitle"].SetBounds(x, (int)(202 * s), w - (int)(60 * s), (int)(20 * s));
            _soundToggle.SetBounds(Width - (int)(62 * s), (int)(200 * s), (int)(46 * s), (int)(26 * s));
            _soundSelect.SetBounds(x, (int)(228 * s), w, (int)(38 * s));
            Controls["volumeLabel"].SetBounds(x, (int)(276 * s), (int)(52 * s), (int)(24 * s));
            _soundVolumeStep.SetBounds((int)(72 * s), (int)(274 * s), (int)(108 * s), (int)(34 * s));
            Controls["volumeUnit"].SetBounds((int)(186 * s), (int)(276 * s), (int)(24 * s), (int)(24 * s));
            _chooseSound.SetBounds((int)(220 * s), (int)(274 * s), (int)(108 * s), (int)(34 * s));
            _testSound.SetBounds((int)(336 * s), (int)(274 * s), (int)(84 * s), (int)(34 * s));
            _soundFileLabel.SetBounds(x, (int)(314 * s), w, (int)(24 * s));

            // 震动子面板：紧随音效分区下方，宽度与本卡片对齐。
            int rumbleY = (int)(356 * s);
            _rumbleChild.SetBounds(x, rumbleY, w, Theme.S(490));
        }

        private static Color ParseFeedbackColor(string hex)
        {
            try
            {
                if (string.IsNullOrEmpty(hex)) return Color.White;
                string h = hex.TrimStart('#');
                if (h.Length == 6)
                {
                    int rr = Convert.ToInt32(h.Substring(0, 2), 16);
                    int gg = Convert.ToInt32(h.Substring(2, 2), 16);
                    int bb = Convert.ToInt32(h.Substring(4, 2), 16);
                    return Color.FromArgb(rr, gg, bb);
                }
            }
            catch { }
            return Color.White;
        }

        private Label MakeTitle(string text)
        {
            Label label = new Label();
            label.Text = text;
            label.AutoSize = false;
            label.BackColor = Theme.SurfaceAlt;
            label.Font = Theme.UiFont(8.8f, true);
            label.ForeColor = Theme.TextSoft;
            label.TextAlign = ContentAlignment.MiddleLeft;
            return label;
        }

        private Label MakeLabel(string text)
        {
            Label label = new Label();
            label.Text = text;
            label.AutoSize = false;
            label.BackColor = Theme.SurfaceAlt;
            label.ForeColor = Theme.Muted;
            label.Font = Theme.UiFont(8.2f, false);
            label.TextAlign = ContentAlignment.MiddleLeft;
            return label;
        }

        private Label MakeUnit(string text)
        {
            Label label = new Label();
            label.Text = text;
            label.AutoSize = false;
            label.BackColor = Theme.SurfaceAlt;
            label.ForeColor = Color.White;
            label.Font = Theme.UiFont(8.0f, false);
            label.TextAlign = ContentAlignment.MiddleCenter;
            return label;
        }

        private void RaiseChanged()
        {
            EventHandler h = Changed;
            if (h != null) h(this, EventArgs.Empty);
            Invalidate();
        }
    }
}
