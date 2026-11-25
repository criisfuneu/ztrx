using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

internal static class Program {
    [STAThread] static void Main() {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.ThreadException += delegate(object sender, System.Threading.ThreadExceptionEventArgs e) {
            MessageBox.Show(e.Exception.ToString(), "Falha no iniciador", MessageBoxButtons.OK, MessageBoxIcon.Error);
        };
        try { Application.Run(new Launcher()); }
        catch(Exception e) {
            try { File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "erro-iniciador.txt"), e.ToString()); } catch {}
            MessageBox.Show(e.ToString(), "Falha no iniciador", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}

internal sealed class Launcher : Form {
    readonly string root = AppDomain.CurrentDomain.BaseDirectory;
    readonly TextBox game = new TextBox();
    readonly TextBox output = new TextBox();
    readonly Label state = new Label();
    readonly Button start = new Button();
    readonly Button stop = new Button();
    readonly Button browse = new Button();
    readonly ComboBox mode = new ComboBox();
    readonly ConcurrentQueue<string> lines = new ConcurrentQueue<string>();
    readonly Timer timer = new Timer();
    volatile Process child;
    volatile bool cancelled;
    bool busy;
    string latestLog;

    public Launcher() {
        Text = "GTA VI - teste AMD";
        ClientSize = new Size(780, 500);
        MinimumSize = new Size(680, 420);
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 10);
        Label heading = new Label { Text = "Renderizador AMD", Location = new Point(18, 14), AutoSize = true, Font = new Font("Segoe UI", 16, FontStyle.Bold) };
        Label hint = new Label { Text = "Na primeira vez, aguarde a preparacao dos shaders.", Location = new Point(19, 47), AutoSize = true };
        game.SetBounds(20, 78, 645, 27);
        game.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        game.Text = @"D:\GTA6\GTA6.Xbox.v1.0.2.1-CYBERLEEK\game_scarlett_master.exe";
        browse.Text = "Escolher";
        browse.SetBounds(675, 76, 85, 31);
        browse.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        browse.Click += delegate { using(OpenFileDialog dialog = new OpenFileDialog()) {
            dialog.Filter = "Executavel do jogo|*.exe";
            if(dialog.ShowDialog(this) == DialogResult.OK) game.Text = dialog.FileName;
        }};
        mode.DropDownStyle = ComboBoxStyle.DropDownList;
        mode.Items.AddRange(new object[] { "Series S - menor carga", "Series X" });
        mode.SelectedIndex = 0;
        mode.SetBounds(20, 119, 220, 30);
        start.Text = "Iniciar";
        start.SetBounds(252, 117, 108, 34);
        start.Click += StartGame;
        stop.Text = "Encerrar";
        stop.SetBounds(370, 117, 108, 34);
        stop.Enabled = false;
        stop.Click += delegate { cancelled = true; Process p = child; try { if(p != null && !p.HasExited) p.Kill(); } catch {} };
        Button logs = new Button { Text = "Abrir log", Location = new Point(488, 117), Size = new Size(108,34) };
        logs.Click += delegate { if(!String.IsNullOrEmpty(latestLog) && File.Exists(latestLog)) Process.Start("notepad.exe", "\"" + latestLog + "\""); };
        state.SetBounds(20, 162, 740, 45);
        state.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        state.Text = "Pronto. A janela continua aberta se o jogo encerrar.";
        output.SetBounds(20, 213, 740, 267);
        output.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
        output.Multiline = true; output.ReadOnly = true; output.ScrollBars = ScrollBars.Vertical;
        output.Font = new Font("Consolas", 8);
        Controls.AddRange(new Control[] { heading, hint, game, browse, mode, start, stop, logs, state, output });
        timer.Interval = 350;
        timer.Tick += delegate { string line; StringBuilder batch = new StringBuilder();
            for(int i=0; i<120 && lines.TryDequeue(out line); ++i) batch.AppendLine(line);
            if(batch.Length > 0) {
                if(output.TextLength > 180000) output.Text = output.Text.Substring(output.TextLength - 60000);
                output.AppendText(batch.ToString());
            }
        };
        timer.Start();
        Shown += delegate { if(File.Exists(game.Text) && File.Exists(Path.Combine(root,"victory.exe"))) StartGame(null,EventArgs.Empty); };
        FormClosing += delegate(object sender, FormClosingEventArgs e) {
            if(!busy) return;
            if(MessageBox.Show(this,"Encerrar o teste em andamento?","Teste AMD",MessageBoxButtons.YesNo,MessageBoxIcon.Question) != DialogResult.Yes) { e.Cancel=true; return; }
            cancelled=true; Process p=child; try { if(p != null && !p.HasExited) p.Kill(); } catch {}
        };
    }

    void Status(string text) { if(!IsDisposed) BeginInvoke((Action)delegate { state.Text = text; }); }

    async void StartGame(object sender, EventArgs e) {
        if(busy) return;
        string title = game.Text.Trim().Trim('"');
        if(Directory.Exists(title)) title = Path.Combine(title,"game_scarlett_master.exe");
        string exe = Path.Combine(root,"victory.exe");
        if(!File.Exists(title) || !File.Exists(exe)) {
            state.Text = !File.Exists(exe) ? "victory.exe ausente. Extraia todos os arquivos do ZIP." : "Selecione o executavel do jogo.";
            return;
        }
        title=Path.GetFullPath(title);
        string console = mode.SelectedIndex==0 ? "series-s" : "series-x";
        busy=true; cancelled=false; start.Enabled=false; browse.Enabled=false; game.Enabled=false; mode.Enabled=false; stop.Enabled=true;
        try {
            Directory.CreateDirectory(Path.Combine(root,"logs"));
            Directory.CreateDirectory(Path.Combine(root,"cache"));
            Directory.CreateDirectory(Path.Combine(root,"data"));
            string runtimeTitle = await Task.Run(() => PrepareRuntime(title));
            string hash;
            using(SHA256 sha=SHA256.Create()) using(FileStream file=File.OpenRead(exe)) hash=BitConverter.ToString(sha.ComputeHash(file)).Replace("-","").Substring(0,16);
            string marker=Path.Combine(root,"cache","warm-"+hash+".ready");
            if(!File.Exists(marker)) {
                Status("Preparando shaders. Isso pode levar varios minutos; acompanhe o log.");
                int warmed = await Task.Run(() => Run(exe,WarmArguments(title),title,"shaders"));
                if(cancelled) { Status("Teste encerrado."); return; }
                if(warmed != 0) { Status("A preparacao encerrou com codigo 0x"+unchecked((uint)warmed).ToString("X8")+". O log continua disponivel."); return; }
                File.WriteAllText(marker,DateTime.UtcNow.ToString("o"));
            }
            if(cancelled) { Status("Teste encerrado."); return; }
            Status("Abrindo o jogo. Mantenha esta janela aberta durante o teste.");
            int code=await Task.Run(() => Run(exe,"run \""+runtimeTitle+"\" --execute --console "+console,title,"jogo"));
            Status("Jogo encerrou com codigo 0x"+unchecked((uint)code).ToString("X8")+". Consulte o log abaixo.");
        } catch(Exception failure) {
            lines.Enqueue(failure.ToString());
            Status("Falha ao iniciar: "+failure.Message);
            if(!String.IsNullOrEmpty(latestLog)) try { File.AppendAllText(latestLog,Environment.NewLine+failure); } catch {}
        } finally {
            child=null; busy=false;
            if(!IsDisposed) { start.Enabled=true; browse.Enabled=true; game.Enabled=true; mode.Enabled=true; stop.Enabled=false; }
        }
    }

    static string WarmArguments(string title) {
        string folder=Path.GetDirectoryName(title);
        string result="warm";
        foreach(string path in new string[] { Path.Combine(folder,"shaders.rpf"), title, Path.Combine(folder,"XboxBvhBuilder.dll"), Path.Combine(folder,"amd_fidelityfx_upscaler_xbox.dll") }) {
            if(!File.Exists(path)) throw new FileNotFoundException("Arquivo de shaders ausente.",path);
            result+=" \""+path+"\"";
        }
        return result+" --jobs 6";
    }

    string PrepareRuntime(string title) {
        string source=Path.GetDirectoryName(title);
        string destination=Path.Combine(root,"title-runtime");
        Directory.CreateDirectory(destination);
        CopyRuntimeFile(title,Path.Combine(destination,Path.GetFileName(title)));
        foreach(string path in Directory.GetFiles(source,"*.dll")) {
            string name=Path.GetFileName(path);
            if(name.EndsWith("_original.dll",StringComparison.OrdinalIgnoreCase)) continue;
            string original=Path.Combine(source,Path.GetFileNameWithoutExtension(name)+"_original.dll");
            CopyRuntimeFile(File.Exists(original)?original:path,Path.Combine(destination,name));
        }
        foreach(string name in new string[] { "MicrosoftGame.config", "appxmanifest.xml" }) {
            string path=Path.Combine(source,name);
            if(File.Exists(path)) CopyRuntimeFile(path,Path.Combine(destination,name));
        }
        return Path.Combine(destination,Path.GetFileName(title));
    }

    static void CopyRuntimeFile(string source,string destination) {
        FileInfo from=new FileInfo(source), to=new FileInfo(destination);
        if(to.Exists && from.Length==to.Length && from.LastWriteTimeUtc==to.LastWriteTimeUtc) return;
        File.Copy(source,destination,true);
        File.SetLastWriteTimeUtc(destination,from.LastWriteTimeUtc);
    }

    int Run(string exe,string arguments,string title,string stage) {
        string log=Path.Combine(root,"logs",stage+"-"+DateTime.Now.ToString("yyyyMMdd-HHmmss-fff")+".log");
        latestLog=log;
        ProcessStartInfo info=new ProcessStartInfo(exe,arguments);
        info.WorkingDirectory=root; info.UseShellExecute=false; info.CreateNoWindow=true;
        info.RedirectStandardOutput=true; info.RedirectStandardError=true;
        string[] inherited=new string[info.EnvironmentVariables.Keys.Count];
        info.EnvironmentVariables.Keys.CopyTo(inherited,0);
        foreach(string key in inherited) if(key.StartsWith("VICTORY_",StringComparison.OrdinalIgnoreCase)) info.EnvironmentVariables.Remove(key);
        info.EnvironmentVariables["VICTORY_GAME_DIR"]=Path.GetDirectoryName(title);
        info.EnvironmentVariables["VICTORY_CACHE_DIR"]=Path.Combine(root,"cache");
        info.EnvironmentVariables["VICTORY_DATA_DIR"]=Path.Combine(root,"data");
        info.EnvironmentVariables["VICTORY_LOG"]="info";
        info.EnvironmentVariables["VICTORY_LATE_WAIT"]="3";
        info.EnvironmentVariables["VICTORY_RELAY"]="0";
        info.EnvironmentVariables["VICTORY_WIDE_BACK"]="1";
        info.EnvironmentVariables["VICTORY_CARD_BUFFERS_MIN_MB"]="1";
        info.EnvironmentVariables["VICTORY_CARD_BUFFERS_MB"]="1024";
        info.EnvironmentVariables["VICTORY_SHADER_LOOP_LIMIT"]="256";
        info.EnvironmentVariables["VICTORY_FRAMETIME_DUMP"]=Path.Combine(root,"logs","frametime-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+".csv");
        using(StreamWriter writer=new StreamWriter(log,false,new UTF8Encoding(false))) using(Process process=new Process()) {
            object gate=new object(); writer.AutoFlush=true;
            DataReceivedEventHandler receive=delegate(object sender,DataReceivedEventArgs e) {
                if(e.Data==null) return;
                lock(gate) writer.WriteLine(e.Data);
                if(lines.Count<800) lines.Enqueue(e.Data);
            };
            process.StartInfo=info; process.OutputDataReceived+=receive; process.ErrorDataReceived+=receive;
            child=process;
            process.Start(); process.BeginOutputReadLine(); process.BeginErrorReadLine();
            process.WaitForExit();
            int code=process.ExitCode; child=null;
            lock(gate) writer.WriteLine("EXIT_CODE=0x"+unchecked((uint)code).ToString("X8"));
            return code;
        }
    }
}
