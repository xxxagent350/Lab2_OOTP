using System.Drawing;
using System.Windows.Forms;
using Lab2.Engine;

namespace Lab2.App;

/// <summary>
/// Интерфейс «помощника»: ввод выражения и строки, проверка на регулярность,
/// поиск и подсветка совпадений.
/// </summary>
public sealed class MainForm : Form
{
    private static readonly Color Accent = Color.FromArgb(46, 91, 255);
    private static readonly Color AccentHover = Color.FromArgb(31, 73, 214);
    private static readonly Color PageBackground = Color.FromArgb(244, 246, 251);
    private static readonly Color MutedText = Color.FromArgb(75, 85, 99);
    private static readonly Color DarkText = Color.FromArgb(31, 41, 55);

    private readonly TextBox _txtPattern = new();
    private readonly RichTextBox _txtInput = new();
    private readonly RichTextBox _rtbResult = new();
    private readonly ListView _listMatches = new();
    private readonly RichTextBox _rtbTree = new();
    private readonly RichTextBox _rtbHelp = new();
    private readonly RichTextBox _rtbMiniHelp = new();
    private readonly Label _lblStatus = new();
    private readonly CheckBox _chkIgnoreCase = new();
    private readonly ComboBox _cmbExamples = new();
    private readonly Button _btnCheck = new();
    private readonly Button _btnClear = new();
    private readonly SplitContainer _contentSplit = new();
    private readonly TabControl _tabs = new();
    private readonly List<Example> _examples = new();
    private bool _suppressMatchSelection;

    public MainForm()
    {
        Text = "Лабораторная работа №2 — Регулярные выражения";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(1000, 720);
        ClientSize = new Size(1180, 720);
        BackColor = PageBackground;
        Font = new Font("Segoe UI", 9F);

        BuildUi();
        ShowNeutralStatus();
        LoadExamples();

        KeyPreview = true;
        KeyDown += OnFormKeyDown;

        Shown += (_, _) =>
        {
            _txtPattern.Focus();
            BeginInvoke(new Action(() =>
            {
                const int panel1Min = 320;
                const int panel2Min = 420;

                if (_contentSplit.Width < panel1Min + panel2Min + _contentSplit.SplitterWidth)
                {
                    return;
                }

                _contentSplit.Panel1MinSize = panel1Min;
                _contentSplit.Panel2MinSize = panel2Min;

                int maxDistance = _contentSplit.Width - _contentSplit.SplitterWidth - panel2Min;
                int wanted = Math.Max(panel1Min, (int)(_contentSplit.Width * 0.42));
                _contentSplit.SplitterDistance = Math.Clamp(wanted, panel1Min, maxDistance);
            }));
        };
    }

    /* ===================== Построение интерфейса ===================== */

    private void BuildUi()
    {
        SuspendLayout();

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Padding = new Padding(12),
            BackColor = PageBackground
        };

        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 112));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 112));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 96));

        root.Controls.Add(CreateHeader(), 0, 0);
        root.Controls.Add(CreatePatternBox(), 0, 1);
        root.Controls.Add(CreateContent(), 0, 2);
        root.Controls.Add(CreateStatusBox(), 0, 3);

        Controls.Add(root);
        ResumeLayout(true);
    }

    private Control CreateHeader()
    {
        var panel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.White,
            BorderStyle = BorderStyle.FixedSingle,
            Padding = new Padding(16, 10, 16, 8),
            Margin = new Padding(0, 0, 0, 8)
        };

        var table = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            BackColor = Color.White
        };

        table.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        table.RowStyles.Add(new RowStyle(SizeType.Absolute, 22));
        table.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var title = new Label
        {
            Text = "Лабораторная работа №2 — Регулярные выражения",
            Dock = DockStyle.Fill,
            AutoSize = false,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font("Segoe UI", 16.5F, FontStyle.Bold),
            ForeColor = DarkText
        };

        var subtitle = new Label
        {
            Text = "Проверка выражения на регулярность и поиск совпадений в строке. " +
                   "Движок собственный: парсер + NFA (Томпсон), без встроенной regex-библиотеки.",
            Dock = DockStyle.Fill,
            AutoSize = false,
            AutoEllipsis = true,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font("Segoe UI", 9.5F),
            ForeColor = MutedText
        };

        var authors = new Label
        {
            Text = "СДП-ПИ-242 · Лохницкий М.И., Коншин И.А.",
            Dock = DockStyle.Fill,
            AutoSize = false,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font("Segoe UI", 8.5F),
            ForeColor = Color.FromArgb(107, 114, 128)
        };

        table.Controls.Add(title, 0, 0);
        table.Controls.Add(subtitle, 0, 1);
        table.Controls.Add(authors, 0, 2);
        panel.Controls.Add(table);
        return panel;
    }

    private Control CreatePatternBox()
    {
        var box = new GroupBox
        {
            Text = "Выражение (шаблон)",
            Dock = DockStyle.Fill,
            BackColor = Color.White,
            Padding = new Padding(10, 6, 10, 8),
            Margin = new Padding(0, 0, 0, 8),
            Font = new Font("Segoe UI", 9F, FontStyle.Bold)
        };

        var table = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 2,
            BackColor = Color.White
        };

        table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        table.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        table.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var label = new Label
        {
            Text = "Шаблон:",
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Font = new Font("Segoe UI", 9.5F, FontStyle.Bold),
            ForeColor = DarkText,
            Margin = new Padding(0, 0, 8, 0)
        };

        _txtPattern.Dock = DockStyle.Fill;
        _txtPattern.Font = new Font("Consolas", 11F);
        _txtPattern.Margin = new Padding(0, 2, 0, 2);
        _txtPattern.PlaceholderText = @"например: \b\w+@\w+\.\w{2,}\b";
        _txtPattern.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter && !e.Control && !e.Shift && !e.Alt)
            {
                RunCheck();
                e.SuppressKeyPress = true;
            }
        };

        table.Controls.Add(label, 0, 0);
        table.Controls.Add(_txtPattern, 1, 0);

        var flow = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            AutoScroll = true,
            BackColor = Color.White,
            Margin = new Padding(0, 4, 0, 0)
        };

        StylePrimaryButton(_btnCheck, "Проверить");
        _btnCheck.Click += (_, _) => RunCheck();

        StyleSecondaryButton(_btnClear, "Очистить");
        _btnClear.Click += (_, _) => ClearAll();

        _chkIgnoreCase.Text = "Игнорировать регистр";
        _chkIgnoreCase.AutoSize = true;
        _chkIgnoreCase.Font = new Font("Segoe UI", 9.5F);
        _chkIgnoreCase.ForeColor = DarkText;
        _chkIgnoreCase.Margin = new Padding(4, 8, 18, 0);
        _chkIgnoreCase.CheckedChanged += (_, _) =>
        {
            if (!string.IsNullOrEmpty(_txtPattern.Text))
            {
                RunCheck();
            }
        };

        var exampleLabel = new Label
        {
            Text = "Пример:",
            AutoSize = true,
            Font = new Font("Segoe UI", 9.5F, FontStyle.Bold),
            ForeColor = DarkText,
            Margin = new Padding(0, 9, 6, 0)
        };

        _cmbExamples.Width = 260;
        _cmbExamples.DropDownStyle = ComboBoxStyle.DropDownList;
        _cmbExamples.Font = new Font("Segoe UI", 9.5F);
        _cmbExamples.Margin = new Padding(0, 3, 0, 0);
        _cmbExamples.SelectedIndexChanged += OnExampleSelected;

        flow.Controls.Add(_btnCheck);
        flow.Controls.Add(_btnClear);
        flow.Controls.Add(_chkIgnoreCase);
        flow.Controls.Add(exampleLabel);
        flow.Controls.Add(_cmbExamples);

        table.Controls.Add(flow, 0, 1);
        table.SetColumnSpan(flow, 2);

        box.Controls.Add(table);
        return box;
    }

    private Control CreateContent()
    {
        _contentSplit.Dock = DockStyle.Fill;
        _contentSplit.Orientation = Orientation.Vertical;
        _contentSplit.SplitterWidth = 8;
        _contentSplit.BackColor = PageBackground;
        _contentSplit.Margin = new Padding(0);

        var inputBox = new GroupBox
        {
            Text = "Строка для поиска",
            Dock = DockStyle.Fill,
            BackColor = Color.White,
            Padding = new Padding(8),
            Margin = new Padding(0, 0, 0, 6),
            Font = new Font("Segoe UI", 9F, FontStyle.Bold)
        };

        _txtInput.Dock = DockStyle.Fill;
        _txtInput.Font = new Font("Consolas", 11F);
        _txtInput.ScrollBars = RichTextBoxScrollBars.Both;
        _txtInput.WordWrap = false;
        _txtInput.DetectUrls = false;
        _txtInput.AcceptsTab = true;
        _txtInput.BorderStyle = BorderStyle.None;
        _txtInput.Text = "Съешь ещё этих мягких французских булок.\r\n" +
                         "Контакты: sstat@grsu.by, support@example.com\r\n" +
                         "Телефон: 123-45-678\r\n" +
                         "Кошки и собаки: cat dog cat.";

        inputBox.Controls.Add(_txtInput);

        var miniHelpBox = new GroupBox
        {
            Text = "Мини-справка по синтаксису",
            Dock = DockStyle.Fill,
            BackColor = Color.White,
            Padding = new Padding(8),
            Margin = new Padding(0),
            Font = new Font("Segoe UI", 9F, FontStyle.Bold)
        };

        _rtbMiniHelp.Dock = DockStyle.Fill;
        _rtbMiniHelp.Font = new Font("Consolas", 8.6F);
        _rtbMiniHelp.ReadOnly = true;
        _rtbMiniHelp.BackColor = Color.White;
        _rtbMiniHelp.BorderStyle = BorderStyle.None;
        _rtbMiniHelp.WordWrap = false;
        _rtbMiniHelp.DetectUrls = false;
        _rtbMiniHelp.ScrollBars = RichTextBoxScrollBars.Vertical;
        _rtbMiniHelp.Text = MiniHelpText;

        miniHelpBox.Controls.Add(_rtbMiniHelp);

        var leftLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            BackColor = PageBackground
        };
        leftLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        leftLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        leftLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 220));
        leftLayout.Controls.Add(inputBox, 0, 0);
        leftLayout.Controls.Add(miniHelpBox, 0, 1);

        _contentSplit.Panel1.Controls.Add(leftLayout);

        _tabs.Dock = DockStyle.Fill;
        _tabs.Font = new Font("Segoe UI", 9F);

        _tabs.TabPages.Add(CreateResultTab());
        _tabs.TabPages.Add(CreateTreeTab());
        _tabs.TabPages.Add(CreateHelpTab());

        _contentSplit.Panel2.Controls.Add(_tabs);
        return _contentSplit;
    }

    private TabPage CreateResultTab()
    {
        var tab = new TabPage("Результат")
        {
            BackColor = Color.White,
            Padding = new Padding(6)
        };

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            BackColor = Color.White
        };

        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 150));

        var resultBox = new GroupBox
        {
            Text = "Текст с подсветкой совпадений",
            Dock = DockStyle.Fill,
            BackColor = Color.White,
            Padding = new Padding(8),
            Margin = new Padding(0, 0, 0, 6),
            Font = new Font("Segoe UI", 9F, FontStyle.Bold)
        };

        _rtbResult.Dock = DockStyle.Fill;
        _rtbResult.Font = new Font("Consolas", 11F);
        _rtbResult.ReadOnly = true;
        _rtbResult.BackColor = Color.White;
        _rtbResult.BorderStyle = BorderStyle.None;
        _rtbResult.WordWrap = true;
        _rtbResult.DetectUrls = false;
        _rtbResult.ScrollBars = RichTextBoxScrollBars.Vertical;
        _rtbResult.HideSelection = false;

        resultBox.Controls.Add(_rtbResult);

        var listBox = new GroupBox
        {
            Text = "Список совпадений",
            Dock = DockStyle.Fill,
            BackColor = Color.White,
            Padding = new Padding(8),
            Margin = new Padding(0),
            Font = new Font("Segoe UI", 9F, FontStyle.Bold)
        };

        _listMatches.Dock = DockStyle.Fill;
        _listMatches.View = View.Details;
        _listMatches.FullRowSelect = true;
        _listMatches.GridLines = true;
        _listMatches.HideSelection = false;
        _listMatches.MultiSelect = false;
        _listMatches.Font = new Font("Consolas", 9.5F);
        _listMatches.Columns.Add("№", 44);
        _listMatches.Columns.Add("Позиция", 78);
        _listMatches.Columns.Add("Длина", 66);
        _listMatches.Columns.Add("Фрагмент", 320);
        _listMatches.SelectedIndexChanged += OnMatchSelected;

        listBox.Controls.Add(_listMatches);

        layout.Controls.Add(resultBox, 0, 0);
        layout.Controls.Add(listBox, 0, 1);
        tab.Controls.Add(layout);
        return tab;
    }

    private TabPage CreateTreeTab()
    {
        var tab = new TabPage("Разбор выражения")
        {
            BackColor = Color.White,
            Padding = new Padding(6)
        };

        var box = new GroupBox
        {
            Text = "Дерево разбора (собственный парсер, без regex-библиотеки)",
            Dock = DockStyle.Fill,
            BackColor = Color.White,
            Padding = new Padding(8),
            Font = new Font("Segoe UI", 9F, FontStyle.Bold)
        };

        _rtbTree.Dock = DockStyle.Fill;
        _rtbTree.Font = new Font("Consolas", 10.5F);
        _rtbTree.ReadOnly = true;
        _rtbTree.BackColor = Color.White;
        _rtbTree.BorderStyle = BorderStyle.None;
        _rtbTree.WordWrap = false;
        _rtbTree.DetectUrls = false;
        _rtbTree.ScrollBars = RichTextBoxScrollBars.Both;
        _rtbTree.Text = "Здесь появится дерево разбора после нажатия «Проверить».";

        box.Controls.Add(_rtbTree);
        tab.Controls.Add(box);
        return tab;
    }

    private TabPage CreateHelpTab()
    {
        var tab = new TabPage("Справка")
        {
            BackColor = Color.White,
            Padding = new Padding(6)
        };

        _rtbHelp.Dock = DockStyle.Fill;
        _rtbHelp.Font = new Font("Consolas", 9.5F);
        _rtbHelp.ReadOnly = true;
        _rtbHelp.BackColor = Color.White;
        _rtbHelp.BorderStyle = BorderStyle.None;
        _rtbHelp.WordWrap = true;
        _rtbHelp.DetectUrls = false;
        _rtbHelp.ScrollBars = RichTextBoxScrollBars.Vertical;
        _rtbHelp.Text = HelpText;

        tab.Controls.Add(_rtbHelp);
        return tab;
    }

    private Control CreateStatusBox()
    {
        var box = new GroupBox
        {
            Text = "Сообщение",
            Dock = DockStyle.Fill,
            BackColor = Color.White,
            Padding = new Padding(8),
            Margin = new Padding(0, 8, 0, 0),
            Font = new Font("Segoe UI", 9F, FontStyle.Bold)
        };

        _lblStatus.Dock = DockStyle.Fill;
        _lblStatus.AutoSize = false;
        _lblStatus.AutoEllipsis = true;
        _lblStatus.TextAlign = ContentAlignment.MiddleLeft;
        _lblStatus.Font = new Font("Segoe UI", 10F);
        _lblStatus.Padding = new Padding(10, 6, 10, 6);
        _lblStatus.BackColor = Color.FromArgb(236, 239, 241);
        _lblStatus.ForeColor = Color.FromArgb(55, 71, 79);

        box.Controls.Add(_lblStatus);
        return box;
    }

    private static void StylePrimaryButton(Button button, string text)
    {
        button.Text = text;
        button.Width = 122;
        button.Height = 30;
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderSize = 0;
        button.FlatAppearance.MouseOverBackColor = AccentHover;
        button.BackColor = Accent;
        button.ForeColor = Color.White;
        button.Font = new Font("Segoe UI", 9.5F, FontStyle.Bold);
        button.Cursor = Cursors.Hand;
        button.Margin = new Padding(0, 0, 8, 0);
        button.UseVisualStyleBackColor = false;
    }

    private static void StyleSecondaryButton(Button button, string text)
    {
        button.Text = text;
        button.Width = 100;
        button.Height = 30;
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderSize = 0;
        button.FlatAppearance.MouseOverBackColor = Color.FromArgb(69, 90, 100);
        button.BackColor = Color.FromArgb(96, 125, 139);
        button.ForeColor = Color.White;
        button.Font = new Font("Segoe UI", 9.5F, FontStyle.Bold);
        button.Cursor = Cursors.Hand;
        button.Margin = new Padding(0, 0, 8, 0);
        button.UseVisualStyleBackColor = false;
    }

    /* ===================== Примеры ===================== */

    private void LoadExamples()
    {
        _examples.Add(new Example(
            "E-mail",
            @"\b[\w.+-]+@[\w-]+\.[\w.-]+\b",
            "Контакты:\r\nsstat@grsu.by\r\nsupport@example.com\r\nИван Петров <ivan.petrov@mail.ru>"));

        _examples.Add(new Example(
            "Телефон",
            @"\d{3}-\d{2}-\d{3}",
            "Телефоны: 123-45-678 и 987-65-432."));

        _examples.Add(new Example(
            "Русские слова",
            @"[А-Яа-яЁё]+",
            "Съешь ещё этих мягких французских булок."));

        _examples.Add(new Example(
            "Дата",
            @"\d{2}\.\d{2}\.\d{4}",
            "Даты: 05.10.2026 и 31.12.2027."));

        _examples.Add(new Example(
            "URL",
            @"https?://[^\s]+",
            "Сайты: https://grsu.by и http://example.com/page?x=1"));

        _examples.Add(new Example(
            "Кошки и собаки",
            @"(cat|dog)+",
            "cat dog catdog dogcat"));

        _examples.Add(new Example(
            "Якоря",
            @"^abc$",
            "abc"));

        _examples.Add(new Example(
            "Unicode-категория",
            @"\p{Lu}+",
            "ABC def ПРИВЕТ"));

        _examples.Add(new Example(
            "Не регулярное: обратная ссылка",
            @"(a)\1",
            "aa ab"));

        _examples.Add(new Example(
            "Не регулярное: lookahead",
            @"foo(?=bar)",
            "foobar fooqux"));

        _examples.Add(new Example(
            "Ошибка синтаксиса",
            @"[a-z",
            "abc"));

        _examples.Add(new Example(
            "Пустая строка",
            @"a+",
            string.Empty));

        foreach (var example in _examples)
        {
            _cmbExamples.Items.Add(example);
        }

        _cmbExamples.DisplayMember = nameof(Example.Name);
        _cmbExamples.SelectedIndex = 0;
    }

    private void OnExampleSelected(object? sender, EventArgs e)
    {
        if (_cmbExamples.SelectedItem is not Example example)
        {
            return;
        }

        _txtPattern.Text = example.Pattern;
        _txtInput.Text = example.Text;
        RunCheck();
    }

    /* ===================== Основная проверка ===================== */

    private void RunCheck()
    {
        _listMatches.Items.Clear();
        _rtbResult.Clear();
        _rtbTree.Clear();

        string pattern = _txtPattern.Text;
        if (pattern.Length == 0)
        {
            ShowStatus(StatusKind.Warning, "Введите регулярное выражение.");
            _rtbTree.Text = "Выражение не задано.";
            return;
        }

        RegexPattern compiled;
        try
        {
            compiled = RegexPattern.Parse(pattern, _chkIgnoreCase.Checked);
        }
        catch (RegexParseException ex)
        {
            ShowParseError(ex, pattern);
            _rtbTree.Text = BuildErrorPointer(pattern, ex.Position) +
                            Environment.NewLine + Environment.NewLine + ex.Message;
            return;
        }
        catch (RegexLimitException ex)
        {
            ShowStatus(StatusKind.Error, "Выражение слишком сложное: " + ex.Message);
            _rtbTree.Text = "Разбор невозможен: " + ex.Message;
            return;
        }
        catch (Exception ex)
        {
            ShowStatus(StatusKind.Error, "Внутренняя ошибка разбора: " + ex.Message);
            return;
        }

        _rtbTree.Text =
            "Регулярное выражение корректно." + Environment.NewLine +
            $"Состояний NFA: {compiled.StateCount}." + Environment.NewLine +
            $"Режим: {(_chkIgnoreCase.Checked ? "без учёта регистра" : "с учётом регистра")}." +
            Environment.NewLine + Environment.NewLine +
            compiled.Root.Describe();

        string input = _txtInput.Text;
        if (input.Length == 0)
        {
            _rtbResult.Text = string.Empty;
            ShowStatus(StatusKind.Warning,
                "Выражение является регулярным, но строка пуста. Введите текст для поиска.");
            return;
        }

        IReadOnlyList<RegexMatch> matches;
        try
        {
            matches = compiled.FindAll(input);
        }
        catch (RegexLimitException ex)
        {
            _rtbResult.Text = input;
            ShowStatus(StatusKind.Warning, "Поиск остановлен: " + ex.Message);
            return;
        }
        catch (Exception ex)
        {
            _rtbResult.Text = input;
            ShowStatus(StatusKind.Error, "Внутренняя ошибка поиска: " + ex.Message);
            return;
        }

        RenderMatches(input, matches);

        int nonEmpty = matches.Count(m => m.Length > 0);
        if (matches.Count == 0)
        {
            ShowStatus(StatusKind.Warning,
                "Выражение является регулярным, но в строке не найдено ни одного совпадения.");
        }
        else if (nonEmpty == 0)
        {
            ShowStatus(StatusKind.Warning,
                $"Выражение найдено, но все совпадения имеют нулевую длину ({matches.Count}). Выделять нечего.");
        }
        else
        {
            string limitNote = matches.Count >= RegexPattern.DefaultMaxMatches
                ? " (достигнут лимит показа)"
                : string.Empty;
            string countText = nonEmpty == matches.Count
                ? $"Найдено совпадений: {matches.Count}"
                : $"Найдено совпадений: {matches.Count}, непустых: {nonEmpty}";
            ShowStatus(StatusKind.Success,
                $"Выражение является регулярным. {countText}{limitNote}.");
        }
    }

    private void RenderMatches(string input, IReadOnlyList<RegexMatch> matches)
    {
        _rtbResult.Text = input;

        _rtbResult.SelectAll();
        _rtbResult.SelectionBackColor = Color.White;
        _rtbResult.SelectionColor = Color.Black;
        _rtbResult.DeselectAll();

        _listMatches.BeginUpdate();

        for (int i = 0; i < matches.Count; i++)
        {
            RegexMatch match = matches[i];

            if (match.Length > 0)
            {
                _rtbResult.Select(match.Index, match.Length);
                _rtbResult.SelectionBackColor = Color.FromArgb(255, 235, 59);
                _rtbResult.SelectionColor = Color.Black;
            }

            var item = new ListViewItem((i + 1).ToString());
            item.SubItems.Add((match.Index + 1).ToString());
            item.SubItems.Add(match.Length.ToString());
            item.SubItems.Add(FormatFragment(match));
            item.Tag = match;
            _listMatches.Items.Add(item);
        }

        _listMatches.EndUpdate();
        _rtbResult.DeselectAll();

        if (matches.Count > 0)
        {
            _suppressMatchSelection = true;
            _listMatches.Items[0].Selected = true;
            _listMatches.Items[0].Focused = true;
            _suppressMatchSelection = false;
            _listMatches.EnsureVisible(0);
            ScrollToMatch(matches[0]);
        }
    }

    private void OnMatchSelected(object? sender, EventArgs e)
    {
        if (_suppressMatchSelection)
        {
            return;
        }

        if (_listMatches.SelectedItems.Count > 0 &&
            _listMatches.SelectedItems[0].Tag is RegexMatch match)
        {
            ScrollToMatch(match);
        }
    }

    private void ScrollToMatch(RegexMatch match)
    {
        int textLength = _rtbResult.TextLength;
        if (textLength == 0)
        {
            return;
        }

        int index = Math.Clamp(match.Index, 0, textLength);
        _rtbResult.Select(index, 0);
        _rtbResult.ScrollToCaret();
    }

    private static string FormatFragment(RegexMatch match)
    {
        if (match.Length == 0)
        {
            return "(пусто)";
        }

        string text = match.Value
            .Replace("\r", "\\r")
            .Replace("\n", "\\n")
            .Replace("\t", "\\t");

        return text.Length <= 70 ? text : text[..67] + "...";
    }

    private void ShowParseError(RegexParseException ex, string pattern)
    {
        string position = ex.Position >= 0 && ex.Position < pattern.Length
            ? $"Позиция {ex.Position + 1}."
            : "Позиция: конец выражения.";

        string title = ex.Kind switch
        {
            RegexErrorKind.NotRegular => "Выражение не является регулярным",
            RegexErrorKind.Unsupported => "Конструкция не поддерживается",
            _ => "Выражение не является корректным регулярным выражением"
        };

        StatusKind kind = ex.Kind == RegexErrorKind.Unsupported
            ? StatusKind.Warning
            : StatusKind.Error;

        ShowStatus(kind, $"{title}: {ex.Message} {position}");
    }

    private static string BuildErrorPointer(string pattern, int position)
    {
        int index = Math.Clamp(position, 0, pattern.Length);
        var prefix = new char[index];
        for (int i = 0; i < index; i++)
        {
            prefix[i] = pattern[i] == '\t' ? '\t' : ' ';
        }

        return pattern + Environment.NewLine + new string(prefix) + "^";
    }

    private void ClearAll()
    {
        _txtPattern.Clear();
        _txtInput.Clear();
        _rtbResult.Clear();
        _rtbTree.Clear();
        _listMatches.Items.Clear();
        _cmbExamples.SelectedIndex = -1;
        ShowNeutralStatus();
        _txtPattern.Focus();
    }

    private void OnFormKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.F5 || (e.Control && e.KeyCode == Keys.Enter))
        {
            RunCheck();
            e.SuppressKeyPress = true;
        }
    }

    private void ShowNeutralStatus()
        => ShowStatus(StatusKind.Neutral, "Введите выражение и строку, затем нажмите «Проверить» (F5).");

    private void ShowStatus(StatusKind kind, string text)
    {
        (Color back, Color fore, string icon) = kind switch
        {
            StatusKind.Success => (Color.FromArgb(232, 245, 233), Color.FromArgb(27, 94, 32), "✔"),
            StatusKind.Warning => (Color.FromArgb(255, 248, 225), Color.FromArgb(122, 79, 1), "⚠"),
            StatusKind.Error => (Color.FromArgb(255, 235, 238), Color.FromArgb(183, 28, 28), "✖"),
            _ => (Color.FromArgb(236, 239, 241), Color.FromArgb(55, 71, 79), "ℹ")
        };

        _lblStatus.Text = $"{icon}  {text}";
        _lblStatus.BackColor = back;
        _lblStatus.ForeColor = fore;
    }

    private enum StatusKind
    {
        Neutral,
        Success,
        Warning,
        Error
    }

    private sealed class Example
    {
        public string Name { get; }
        public string Pattern { get; }
        public string Text { get; }

        public Example(string name, string pattern, string text)
        {
            Name = name;
            Pattern = pattern;
            Text = text;
        }
    }

    private const string MiniHelpText = """
        ОСНОВЫ
        .          любой символ, кроме \n
        \d         цифра           \D — не цифра
        \w         буква/цифра/_   \W — наоборот
        \s         пробел/таб/\n   \S — наоборот
        [abc]      один из a,b,c   [^abc] — любой, кроме
        [a-z]      диапазон        [А-Яа-яЁё] — русские

        ПОВТОРЫ
        *          0 или больше    + — 1 или больше
        ?          0 или 1          {3} — ровно 3
        {2,4}      от 2 до 4        {2,} — 2 или больше

        ГРУППЫ И АЛЬТЕРНАТИВА
        (...)      группа           (?:...) — без захвата
        |          или              (cat|dog)+

        ГРАНИЦЫ
        ^          начало текста    $ — конец текста
        \b         граница слова    \. — экранировать спецсимволы
                   . * + ? ( ) [ ] { } | \

        ПРИМЕРЫ
        E-mail     \b[\w.+-]+@[\w-]+\.[\w.-]+\b
        Телефон    \d{3}-\d{2}-\d{3}
        Дата       \d{2}\.\d{2}\.\d{4}
        Слова      [А-Яа-яЁё]+
        URL        https?://[^\s]+

        НЕ РЕГУЛЯРНО
        \1, \k<имя>, (?=...), (?R), (?<a-b>...)
        """;

    private const string HelpText = """
        Как пользоваться
        ================
        Краткая шпаргалка — под строкой поиска; полная — на этой вкладке.
        1. Введите выражение в поле «Шаблон».
        2. Введите текст в поле «Строка для поиска».
        3. Нажмите «Проверить» (F5 или Ctrl+Enter).
        4. Совпадения подсвечиваются жёлтым, их список — на вкладке «Результат».
        5. Вкладка «Разбор выражения» показывает дерево разбора.

        Что проверяется
        ===============
        • Синтаксическая корректность выражения.
        • Регулярность: обратные ссылки, lookaround, условия и рекурсия
          отвергаются, потому что выводят язык за пределы регулярных.
        • Пустая строка: выводится предупреждение.
        • Если совпадений нет: выводится сообщение об отсутствии.
        • Совпадения нулевой длины (например, a* на "bbb") показываются
          в списке, но подсвечивать их нечего.

        Поддерживаемый синтаксис
        =======================
        Символы:        a b c, . (любой символ, кроме \n)
        Классы:         [abc], [^abc], [a-z], [А-Яа-яЁё]
        Сокращения:     \d \D \w \W \s \S
        Unicode:        \p{L}, \p{Lu}, \p{Nd}, \P{L} и другие категории
        Квантификаторы: * + ? {n} {n,} {n,m}, ленивые *? +? ?? {n,m}?
        Группы:         (...) (?:...) (?<имя>...) (?'имя'...)
        Альтернатива:   |
        Якоря:          ^ $ \A \z \Z \b \B
        Escape:         \n \r \t \f \v \0 \a \e \x41 \u0410 \cA
                        \. \* \+ \? \( \) \[ \] \{ \} \| \\ \^ \$ \- \/

        Поиск
        =====
        Движок ищет самое левое и самое длинное неперекрывающееся совпадение,
        затем продолжает поиск с конца предыдущего. Поэтому a+? синтаксически
        принимается, но результат будет таким же, как у a+.

        Ограничения
        ===========
        • Длина строки: до 20 000 символов.
        • Длина выражения: до 100 000 символов.
        • Вложенность групп: до 500.
        • Состояний NFA: до 50 000.
        • Шагов поиска: до 5 000 000.
        • Показывается до 2 000 совпадений.
        • Регистр: флажок «Игнорировать регистр».
        • Встроенные флаги (?i), (?m), (?s) не поддерживаются.

        Не регулярные / не поддерживаемые
        =================================
        • \1, \2, \k<имя>, \g<...>, (?P=...) — обратные ссылки.
        • (?=...) (?!...) (?<=...) (?<!...) — lookaround.
        • (?(...)...) — условные конструкции.
        • (?R), (?1) — рекурсия.
        • (?>...) — атомарные группы.
        • a++ — possessive-квантификаторы.
        • (?i), (?m), (?s) — встроенные флаги.

        Внутреннее устройство
        =====================
        Lab2.Engine содержит собственный рекурсивный парсер, построение
        NFA по методу Томпсона и эмуляцию автомата без встроенной
        regex-библиотеки .NET.
        """;
}
