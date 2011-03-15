using System;
using System.Configuration;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using System.Drawing;
using System.Net;
using System.IO;
using System.Threading;
using System.Diagnostics;


namespace JenkinsTray
{
    public class JenkinsTrayApp : Form
    {
        [STAThread]
        public static void Main()
        {
            try
            {
                Application.Run(new JenkinsTrayApp());
            }
            catch (Exception e)
            {
                MessageBox.Show(e.StackTrace);
            }
        }

        private NotifyIcon trayIcon;
        private ContextMenu trayMenu;
        private bool jenkinsIsBuilding = false;
        private bool lastBuildIsOK = false;

        private volatile bool shouldStop = false;
        private Thread workerThread;
        private string jenkinsUrl;
        private int updatePeriod;
        private String[] currentJobs;
        private String jobsWithError;
        private bool queueExists;
        private String jobsInQueue;
        private bool unknownState;


        private Icon greenfree = new Icon(Properties.Resources.greenfree, 40 , 40);
        private Icon greenwork = new Icon(Properties.Resources.greenwork, 40 , 40 );
        private Icon redfree = new Icon(Properties.Resources.redfree, 40, 40);
        private Icon redwork = new Icon(Properties.Resources.redwork, 40, 40);
        private Icon unknown = new Icon(Properties.Resources.unknown, 40, 40);
        
        public JenkinsTrayApp()
        {
            // Create a simple tray menu with only one item.
            trayMenu = new ContextMenu();
            trayMenu.MenuItems.Add("Open Jenkins server", OnOpen);
            trayMenu.MenuItems.Add("Open config file", OnConfig);
            trayMenu.MenuItems.Add("Quit", OnExit);

            // Create a tray icon. In this example we use a
            // standard system icon for simplicity, but you
            // can of course use your own custom icon too.
            trayIcon = new NotifyIcon();
            
            jenkinsUrl = ConfigurationManager.AppSettings.Get("jenkinsUrl");
            updatePeriod = Convert.ToInt32(ConfigurationManager.AppSettings.Get("updatePeriod"));
            currentJobs = ConfigurationManager.AppSettings.Get("currentJobs").Split(' ');
            
            workerThread = new Thread(this.DoWork);
            workerThread.Start();

            // Add menu to tray icon and show it.
            trayIcon.ContextMenu = trayMenu;
            trayIcon.Visible = true;
            trayIcon.Icon = new Icon(JenkinsTray.Properties.Resources.unknown, 40, 40);
            trayIcon.DoubleClick += this.OnOpen;
            
            InitializeComponent();
        }

        public void DoWork()
        {
            while (!shouldStop)
            {
                CheckJenkinsWorkStatus();
                CheckJenkinsLastBuildStatus();
                ShowStatus();
                Thread.Sleep(updatePeriod);
            }
        }

        private void ShowStatus()
        {
            if (StateIsUnknown())
            {
                trayIcon.Text = String.Format("Unable to read status from Jenkins server {0}", jenkinsUrl);
                SetTrayIcon(unknown);
                return;
            }

            String hoverText;              
            if (lastBuildIsOK)
            {
                hoverText = "Last build(s) OK\n";
                if (jenkinsIsBuilding)
                {
                    hoverText += "Building now...\n";
                    if (queueExists)
                        hoverText += "Queue:\n" + jobsInQueue;
                    SetTrayIcon(greenwork);
                } else {
                    hoverText += "Ready to build!";
                    SetTrayIcon(greenfree);                
                }
            }
            else
            {
                hoverText = String.Format("Build(s) {0} have errors\n", jobsWithError);
                if (jenkinsIsBuilding)
                {
                    hoverText += "Building now...\n";
                    if (queueExists)
                        hoverText += "Queue:\n" + jobsInQueue;
                    SetTrayIcon(redwork);
                }
                else
                {
                    hoverText += "Ready to build!";
                    SetTrayIcon(redfree);
                }
 
            }
            if (hoverText.Length >= 64) hoverText = hoverText.Substring(0, 63);
            trayIcon.Text = hoverText;

        }

        

        private void CheckJenkinsWorkStatus()
        {
            try
            {
                String jenkinsIsIdle = urlToString(jenkinsUrl + "/computer/(master)/api/xml?xpath=/masterComputer/idle/text()");
                jenkinsIsBuilding = !Convert.ToBoolean(jenkinsIsIdle);
 
                String queue = urlToString(jenkinsUrl + "/queue/api/xml");
                queueExists = !queue.Equals("<queue></queue>");
                jobsInQueue = "";
                if (queueExists)
                {
                    String[] splitTokens = { "<name>"}; 
                    String[] names = queue.Split(splitTokens, StringSplitOptions.None);
                    for (int i = names.Length-1; i > 0; i--)
                    {
                        jobsInQueue += names[i].Substring(0, names[i].IndexOf("<")) + "\n";
                    }
                }
            } catch (Exception ){
                SetStateUnknown();
            }
        }


        private void CheckJenkinsLastBuildStatus()
        {
            try
            {
                String xpath;
                jobsWithError = "";
                lastBuildIsOK = true;
                foreach (String buildJob in currentJobs)
                {
                    xpath = "/api/xml?xpath=/*/lastCompletedBuild/number";
                    String lastCompleted = urlToString(jenkinsUrl + "/job/" + buildJob + xpath);


                    xpath = "/api/xml?xpath=/*/lastSuccessfulBuild/number";
                    String lastSuccessful = urlToString(jenkinsUrl + "/job/" + buildJob + xpath);

                    if ((lastCompleted.Length == 0) || (lastSuccessful.Length == 0)){
                        lastBuildIsOK = false;
                        jobsWithError += buildJob;
                        continue;
                    }

                    if (lastCompleted.Equals(lastSuccessful) && (lastCompleted.Length > 0)) {
                        lastBuildIsOK = lastBuildIsOK && true;
                    }else
                    {
                        lastBuildIsOK = false;
                        jobsWithError += buildJob;
                    }
                }
                SetStateKnown();
            }
            catch (Exception)
            {
                SetStateUnknown();
            }

        }


        private void SetTrayIcon(Icon icon)
        {
            if (trayIcon.Icon != icon)
            {
                if (OnWindows7())
                {
                    trayIcon.ShowBalloonTip(1, "Jenkins says:", "Somethings happens!", ToolTipIcon.Info);
                }
            }
            trayIcon.Icon = icon;
        }


        private bool OnWindows7 (){
            OperatingSystem osInfo = Environment.OSVersion;
            return ((osInfo.Platform == System.PlatformID.Win32NT) &&
                    (osInfo.Version.Major == 6) &&
                    (osInfo.Version.Minor == 1));
   
        }

        
        private void OnExit(object sender, EventArgs e)
        {
            shouldStop = true;
            Thread.Sleep(10);
            workerThread.Join();

            Application.Exit();
        }

        private void OnOpen(object sender, EventArgs e)
        {
            System.Diagnostics.Process.Start(jenkinsUrl);
        }

        private void OnConfig(object sender, EventArgs e)
        {
            Process p = new Process();
            p.StartInfo.WorkingDirectory = Application.StartupPath;
            p.StartInfo.FileName = "notepad.exe";
            p.StartInfo.Arguments = "JenkinsTray.exe.config";
            p.Start();
            p.WaitForExit();

            shouldStop = true;
            Thread.Sleep(10);
            workerThread.Join();

            Application.Restart();
        }


        protected override void Dispose(bool isDisposing)
        {
            if (isDisposing)
            {
                // Release the icon resource.
                trayIcon.Dispose();
            }

            base.Dispose(isDisposing);
        }

        private static String urlToString(String url)
        {
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
            StreamReader reader = new StreamReader(request.GetResponse().GetResponseStream());
            return reader.ReadToEnd().Trim();
        }

        private void SetStateKnown()
        {
            unknownState = false;
        }

        private void SetStateUnknown()
        {
            unknownState = true;
        }

        private bool StateIsUnknown()
        {
            return unknownState == true;
        }

        protected override void OnLoad(EventArgs e)
        {
            Visible = false; // Hide form window.
            ShowInTaskbar = false; // Remove from taskbar.

            base.OnLoad(e);
        }

        private void InitializeComponent()
        {
            this.SuspendLayout();
            this.ClientSize = new System.Drawing.Size(284, 262);
            this.Name = "JenkinsTrayApp";
            this.ResumeLayout(false);
        }
    }
}
