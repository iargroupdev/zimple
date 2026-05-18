//using MessageBox = System.Windows.MessageBox;
//using ContextMenu = System.Windows.Forms.ContextMenu;
using libplctag;
using libplctag.DataTypes;
using libplctag.DataTypes.Simple;
using log4net.Core;
using MES_HAI;
using MES_HAI.Entity;
using Microsoft.SqlServer.Server;
using Microsoft.VisualBasic;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.UI.WebControls;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;

using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Application = System.Windows.Application;
using Label = System.Windows.Controls.Label;
using TextBox = System.Windows.Controls.TextBox;

namespace Zimple
{
    /// <summary>
    /// Interaction logic for ConnectionOP.xaml
    /// </summary>
    public partial class ConnectionOP : UserControl
    {
        private static readonly TimeSpan ConnectionCheckInterval = TimeSpan.FromSeconds(5);
        private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(5);
        private static readonly TimeSpan TriggerScanInterval = TimeSpan.FromSeconds(1);
        private const int MaxOperationNames = 10;
        private const int MaxMeasurementDataItems = 100;

        private readonly System.Threading.SemaphoreSlim connectionGate = new System.Threading.SemaphoreSlim(1, 1);
        private DispatcherTimer checkConnectionTimer = new DispatcherTimer();
        private bool isConnected = false;  // Track connection status
        private bool hasAttemptedInitialConnection = false;  // Track if an initial connection attempt has been made
        public TagDint myTag; //will be used for heartbeat

        private DispatcherTimer checkTriggerTimer = new DispatcherTimer();
        //private TagDint CIMPLE_trigger; // This will be the tag for CIMPLE_trigger

        private string plcIpAddress;
        private PlcTagStore tagStore;

        private readonly Dictionary<string, SemaphoreSlim> opExecutionGates = new Dictionary<string, SemaphoreSlim>();

        private DispatcherTimer heartbeatTimer;
        private readonly SemaphoreSlim heartbeatGate = new SemaphoreSlim(1, 1);
        private CancellationTokenSource queueCancellation;
        private Task queueWorker;
        private volatile bool isProcessingQueue = false;
        private bool isShuttingDown = false;
        private string lastDisplayedOperations = string.Empty;

        private bool isUiReady = false;

        private Dictionary<string, TextBox> opLogBoxes = new Dictionary<string, TextBox>();
        private TextBox globalLogBox;

        private readonly ConcurrentQueue<(string opName, int trigger)> triggerQueue = new ConcurrentQueue<(string opName, int trigger)>();
        private readonly SemaphoreSlim queueSignal = new SemaphoreSlim(0);

        public enum Results
        {
            PA, // Pass
            FA, // Fail
            SC
        }


        public ConnectionOP()
        {
            InitializeComponent();
            LoadSettings();
            this.Loaded += ConnectionOP_Loaded;
            this.Unloaded += ConnectionOP_Unloaded;
        }

        // unica adicao

        public event System.Action<bool> ConnectionStatusChanged;

        public void ConnectFromParent()
        {
            Connect_Click(null, null);
        }

        private void UpdateConnectionStatus(bool connected)
        {
            if (statusIndicator == null || connectionStatusText == null || connectButton == null)
                return;

            if (connected)
            {
                statusIndicator.Fill = new SolidColorBrush(Colors.Green);
                connectionStatusText.Text = "Connected";
                connectButton.IsEnabled = false;
            }
            else
            {
                statusIndicator.Fill = new SolidColorBrush(Colors.Red);
                connectionStatusText.Text = "Disconnected";
                connectButton.IsEnabled = true;
            }

            // NOTIFICAR TAB
            ConnectionStatusChanged?.Invoke(connected);
        }

        private void CreateOpDebugTabs(string[] opNames)
        {
            Dispatcher.Invoke(() =>
            {
                debugTabControl.Items.Clear();
                opLogBoxes.Clear();

                // GLOBAL TAB
                globalLogBox = CreateLogTextBox();
                debugTabControl.Items.Add(new TabItem
                {
                    Header = "Global",
                    Content = globalLogBox
                });

                if (opNames == null)
                    return;

                foreach (var op in opNames)
                {
                    if (string.IsNullOrWhiteSpace(op))
                        continue;

                    var tb = CreateLogTextBox();

                    debugTabControl.Items.Add(new TabItem
                    {
                        Header = op,
                        Content = tb
                    });

                    opLogBoxes[op] = tb;
                }
            });
        }

        private TextBox CreateLogTextBox()
        {
            return new TextBox
            {
                IsReadOnly = true,
                AcceptsReturn = true,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                TextWrapping = TextWrapping.Wrap
            };
        }

        public enum LogLevel
        {
            DEBUG,
            INFO,
            WARN,
            ERROR
        }

        // controla quanto queres ver
        private LogLevel CurrentLogLevel = LogLevel.DEBUG;

        private string SanitizeFileName(string name)
        {
            foreach (char c in System.IO.Path.GetInvalidFileNameChars())
            {
                name = name.Replace(c, '_');
            }
            return name;
        }

        private void WriteLogToFile(string opName, string message, LogLevel level = LogLevel.INFO)
        {
            try
            {
                // filtrar por nível
                if (level < CurrentLogLevel)
                    return;

                string baseFolder = Environment.GetFolderPath(
                    Environment.SpecialFolder.LocalApplicationData);

                string appFolder = System.IO.Path.Combine(baseFolder, "Zimple");

                // fallback se não houver IP (muito importante)
                string ipFolder = string.IsNullOrEmpty(plcIpAddress) ? "NO_IP" : plcIpAddress;

                string logFolder = System.IO.Path.Combine(appFolder, "Diagnostics", ipFolder);

                System.IO.Directory.CreateDirectory(logFolder);

                string fileName = string.IsNullOrEmpty(opName)
                    ? "Global.log"
                    : $"{SanitizeFileName(opName)}.log";

                string fullPath = System.IO.Path.Combine(logFolder, fileName);

                // metadata rica (isto é ouro para debugging)
                string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
                int threadId = Thread.CurrentThread.ManagedThreadId;

                string formattedMessage =
                    $"[{timestamp}] [{level}] [T{threadId}] {message}";

                System.IO.File.AppendAllText(fullPath, formattedMessage + Environment.NewLine);
            }
            catch (Exception ex)
            {
                try
                {
                    // fallback de emergência (nunca perder erro de logging)
                    string fallbackPath = System.IO.Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                        "Zimple_LogError.txt");

                    System.IO.File.AppendAllText(fallbackPath,
                        $"[{DateTime.Now}] LOGGING FAILURE: {ex}\n");
                }
                catch
                {
                    // último fallback: ignorar mesmo
                }
            }
        }

        private void AppendToTextBox(TextBox tb, string text)
        {
            if (tb == null)
                return;

            tb.AppendText(text);
            tb.ScrollToEnd();

            if (tb.LineCount > MaxLogLines)
            {
                int linesToRemove = tb.LineCount - MaxLogLines;
                int index = tb.GetCharacterIndexFromLineIndex(linesToRemove);

                tb.Select(0, index);
                tb.SelectedText = "";
            }
        }

        private void ConnectionOP_Loaded(object sender, RoutedEventArgs e)
        {
            isUiReady = true;
            isShuttingDown = false;
            InitializeConnectionTimer();
            StartQueueWorker();
        }

        private void ConnectionOP_Unloaded(object sender, RoutedEventArgs e)
        {
            ShutdownConnection();
        }


        private void InitializeConnectionTimer()
        {
            checkConnectionTimer.Interval = ConnectionCheckInterval;
            checkConnectionTimer.Tick -= CheckConnection_Tick;
            checkConnectionTimer.Tick += CheckConnection_Tick;
            checkConnectionTimer.Start();  // Start the timer to check connection periodically
        }

        private async void Connect_Click(object sender, RoutedEventArgs e)
        {
            // impedir múltiplos connects simultâneos
            if (!connectionGate.Wait(0))
                return;

            try
            {
                connectButton.IsEnabled = false;

                string plcIp = $"{ipPart1.Text}.{ipPart2.Text}.{ipPart3.Text}.{ipPart4.Text}";
                plcIpAddress = plcIp;
                hasAttemptedInitialConnection = true;

                if (!IsValidIp(plcIp))
                {
                    ForceDisconnect();
                    Log(null, "Invalid IP address format.\n");
                    return;
                }

                await AttemptConnectionAsync(plcIp);

                // proteção extra
                if (!isConnected || tagStore == null)
                {
                    ForceDisconnect();
                    Log(null, $"Unable to connect to PLC at {plcIp}\n");
                    return;
                }

                // conexão válida
                ClearOperationsUI();

                try
                {
                    // dupla verificação defensiva
                    if (tagStore == null)
                    {
                        Log(null, "TagStore is null after connection.\n");
                        ForceDisconnect();
                        return;
                    }

                    var opNamesTag = tagStore.GetStringArray("CIMPLE.OPnames", MaxOperationNames);
                    string[] opNames = tagStore.ReadStringArray(opNamesTag);
                   
                    DisplayOpNames(opNames);
                    CreateOpDebugTabs(opNames);
                }
                catch (Exception ex)
                {
                    Log(null, $"Error reading OP names: {ex.Message}\n");
                }

                InitializeTriggerCheckTimer(plcIp);

                
            }
            catch (Exception ex)
            {
                ForceDisconnect();
                Log(null, $"Unexpected connection error: {ex.Message}\n");
            }
            finally
            {
                connectButton.IsEnabled = true;
                connectionGate.Release();
            }
        }

        private const int MaxLogLines = 500;

        private void Log(string opName, string message)
        {
            string formatted = AddTimestamp(message);

            Dispatcher.Invoke(() =>
            {
                AppendToTextBox(globalLogBox, formatted);

                if (!string.IsNullOrEmpty(opName) && opLogBoxes.ContainsKey(opName))
                {
                    AppendToTextBox(opLogBoxes[opName], formatted);
                }
            });

            WriteLogToFile(opName, formatted);
        }

        private void InitializeHeartbeatTimer()
        {
            if (heartbeatTimer != null)
                return; // já existe, não criar outro

            heartbeatTimer = new DispatcherTimer
            {
                Interval = HeartbeatInterval
            };

            heartbeatTimer.Tick += CheckHeartbeat_Tick;
            heartbeatTimer.Start();
        }

        private async void CheckHeartbeat_Tick(object sender, EventArgs e)
        {
            if (isProcessingQueue)
                return;

            if (!await heartbeatGate.WaitAsync(0))
                return;

            try
            {
                bool ok = await Task.Run(() => CheckHeartbeatCore());
                isConnected = ok;
                UpdateConnectionStatus(ok);
            }
            finally
            {
                heartbeatGate.Release();
            }
        }

        private bool CheckHeartbeatCore()
        {
            try
            {
                PlcTagStore store = tagStore;
                TagDint heartbeatTag = myTag;
                if (store == null || heartbeatTag == null)
                    return false;

                // O heartbeat É o teste real de conectividade
                int tagValue = store.ReadDint(heartbeatTag);

                // Se o PLC limpou o heartbeat, voltamos a levantá-lo
                if (tagValue == 0)
                {
                    store.WriteDint(heartbeatTag, 1);
                }

                return true;
            }
            catch (Exception ex)
            {
                // Qualquer exceção aqui significa: PLC não acessível neste momento
                Console.WriteLine($"Error in CheckHeartbeat: {ex.Message}");
                return false;
            }
        }

        private bool AttemptConnectionCore(string ip)
        {
            try
            {
                // Teste rápido de reachability (ping)
                if (!IsPlcReachable(ip))
                    return false;

                // Limpar gates de execução (novo contexto)
                lock (opExecutionGates)
                {
                    opExecutionGates.Clear();
                }

                // Limpar TagStore anterior
                if (tagStore != null)
                {
                    try
                    {
                        tagStore.Dispose();
                    }
                    catch
                    {
                        // não deixar falhar por dispose
                    }
                    finally
                    {
                        tagStore = null;
                    }
                }

                // Criar novo TagStore
                tagStore = new PlcTagStore(ip);

                // Criar tag de heartbeat (aqui pode crashar se PLC não for compatível)
                myTag = tagStore.GetDint("CIMPLE.Heartbeat");

                // Testar leitura real
                int value = tagStore.ReadDint(myTag);

                // Se PLC limpou heartbeat, voltar a levantar
                if (value == 0)
                    tagStore.WriteDint(myTag, 1);

                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"AttemptConnectionCore error: {ex.Message}");

                // Garantir limpeza total se algo falhar
                try
                {
                    tagStore?.Dispose();
                }
                catch { }

                tagStore = null;
                myTag = null;

                return false;
            }
        }


        private async Task AttemptConnectionAsync(string ip)
        {
            bool ok = await Task.Run(() => AttemptConnectionCore(ip));
            isConnected = ok;

            Dispatcher.Invoke(() =>
            {
                UpdateConnectionStatus(ok);

                if (ok)
                {
                    InitializeHeartbeatTimer();
                }
            });
        }

        // Helper method to check if the PLC is reachable
        private bool IsPlcReachable(string ip)
        {
            try
            {
                using (Ping ping = new Ping())
                {
                    PingReply reply = ping.Send(ip, 500); // Ping with a timeout of 500 milliseconds
                    if (reply.Status == IPStatus.Success)
                    {

                        //StartTriggerChecking();
                    }
                    return reply.Status == IPStatus.Success;
                }
            }
            catch (Exception)
            {
                return false;
            }
        }

        private void ClearOperationsUI()
        {
            if (!isUiReady)
                return;

            Dispatcher.Invoke(() =>
            {
                if (opNamesTextBox != null)
                {
                    opNamesTextBox.Text = "Active Operations: No OPs connected";
                    opNamesTextBox.FontStyle = FontStyles.Italic;
                }

                cimpleOperationNames?.Clear();
            });
        }

        private void ForceDisconnect()
        {
            isConnected = false;

            try
            {
                if (heartbeatTimer != null)
                {
                    heartbeatTimer.Stop();
                    heartbeatTimer.Tick -= CheckHeartbeat_Tick;
                    heartbeatTimer = null;
                }

                checkTriggerTimer.Stop();
                checkTriggerTimer.Tick -= CheckTrigger_Tick;
            }
            catch { }

            while (triggerQueue.TryDequeue(out _))
            {
            }

            try
            {
                tagStore?.Dispose();
                tagStore = null;
            }
            catch { }

            ClearOperationsUI();
            UpdateConnectionStatus(false);
        }

        private void ShutdownConnection()
        {
            isShuttingDown = true;

            try
            {
                checkConnectionTimer.Stop();
                checkConnectionTimer.Tick -= CheckConnection_Tick;
            }
            catch { }

            StopQueueWorker();
            ForceDisconnect();
        }

        private async void CheckConnection_Tick(object sender, EventArgs e)
        {
            if (!hasAttemptedInitialConnection)
                return;

            if (isShuttingDown)
                return;

            string currentIp = $"{ipPart1.Text}.{ipPart2.Text}.{ipPart3.Text}.{ipPart4.Text}";

            if (!IsValidIp(currentIp))
                return;

            if (isConnected)
                return;

            if (!connectionGate.Wait(0))
                return;

            try
            {
                bool ok = await Task.Run(() => AttemptConnectionCore(currentIp));
                isConnected = ok;
                plcIpAddress = currentIp;

                Dispatcher.Invoke(() =>
                {
                    UpdateConnectionStatus(ok);
                    if (ok)
                    {
                        InitializeHeartbeatTimer();
                        InitializeTriggerCheckTimer(currentIp);
                    }
                });
            }
            catch (Exception ex)
            {
                isConnected = false;
                Dispatcher.Invoke(() =>
                {
                    UpdateConnectionStatus(false);
                    Log(null, $"Connection tick error: {ex.Message}\n");
                });
            }
            finally
            {
                connectionGate.Release();
            }
        }


        private bool IsValidIp(string addr)
        {
            IPAddress ip;
            return IPAddress.TryParse(addr, out ip);
        }



        private void IpPart_TextChanged(object sender, TextChangedEventArgs e)
        {
            System.Windows.Controls.TextBox textBox = sender as System.Windows.Controls.TextBox;
            if (textBox != null && !string.IsNullOrWhiteSpace(textBox.Text))
            {
                if (!int.TryParse(textBox.Text, out int ipPart) || ipPart < 0 || ipPart > 255)
                {
                    textBox.Text = "";  // Clear the input if it's not valid
                }
            }

            ForceDisconnect();
        }


        private void IpPart_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            // Only allow numeric input
            if (!char.IsDigit(e.Text, e.Text.Length - 1))
            {
                e.Handled = true;
            }
        }

        // Global list to store CIMPLE operation names
        List<string> cimpleOperationNames = new List<string>();

        private string AddTimestamp(string message)
        {
            string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            return $"[{timestamp}] {message}";
        }


        // Remove the InitializeTriggerCheckTimer method

        private void InitializeTriggerCheckTimer(string ip)
        {

            checkTriggerTimer.Stop();
            checkTriggerTimer.Tick -= CheckTrigger_Tick;
            checkTriggerTimer.Interval = TriggerScanInterval;
            checkTriggerTimer.Tick += CheckTrigger_Tick;
            checkTriggerTimer.Start();
        }

        // campos
        private readonly System.Threading.SemaphoreSlim triggerGate = new System.Threading.SemaphoreSlim(1, 1);

        private async void CheckTrigger_Tick(object sender, EventArgs e)
        {
            if (!isConnected) return;
            if (isShuttingDown) return;
            if (isProcessingQueue) return;
            if (!triggerGate.Wait(0)) return; // evita reentrância do scan

            try
            {
                PlcTagStore store = tagStore;
                if (store == null)
                    return;

                await Task.Run(() =>
                {
                    // ler OPs
                    var opNamesTag = store.GetStringArray("CIMPLE.OPnames", MaxOperationNames);
                    string[] opNames = store.ReadStringArray(opNamesTag);

                    DisplayOpNames(opNames);

                    if (opNames == null) return;

                    foreach (var opName in opNames)
                    {
                        if (string.IsNullOrEmpty(opName))
                            continue;

                        try
                        {
                            SemaphoreSlim opGate;
                            lock (opExecutionGates)
                            {
                                if (!opExecutionGates.TryGetValue(opName, out opGate))
                                {
                                    opGate = new SemaphoreSlim(1, 1);
                                    opExecutionGates[opName] = opGate;
                                }
                            }

                            var statusTag = store.GetString($"{opName}.Status");
                            var triggerTag = store.GetDint($"{opName}.Trigger");

                            int triggerValue = store.ReadDint(triggerTag);
                            string status = store.ReadString(statusTag);

                            if (triggerValue != 0 && status == "ready")
                            {
                                // Marca como processing para evitar retrigger imediato pelo PLC
                                store.WriteString(statusTag, "processing");

                                // Enfileira em vez de executar já
                                triggerQueue.Enqueue((opName, triggerValue));
                                queueSignal.Release();

                                // Opcional: log para debugging
                                Dispatcher.Invoke(() =>
                                    Log(opName, $"Trigger {triggerValue} enqueued\n"));
                            }
                            else if (triggerValue == 0 && status != "ready")
                            {
                                store.WriteString(statusTag, "ready");
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"OP {opName} scan error: {ex.Message}");
                        }
                    }
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
                Dispatcher.Invoke(() =>
                    Log(null, $"Error scanning triggers: {ex.Message}\n"));
            }
            finally
            {
                triggerGate.Release();
            }
        }

        private void DisplayOpNames(string[] opNames)
        {
            if (!isUiReady)
                return;

            string displayText;
            System.Windows.FontStyle fontStyle;

            if (opNames != null && opNames.Any(op => !string.IsNullOrWhiteSpace(op)))
            {
                displayText =
                    "Active Operations:\n" +
                    string.Join(Environment.NewLine,
                        opNames.Where(op => !string.IsNullOrWhiteSpace(op)));

                fontStyle = FontStyles.Normal;
            }
            else
            {
                displayText = "Active Operations: No OPs connected";
                fontStyle = FontStyles.Italic;
            }

            if (displayText == lastDisplayedOperations)
                return;

            lastDisplayedOperations = displayText;

            Dispatcher.BeginInvoke(new System.Action(() =>
            {
                if (opNamesTextBox == null)
                    return;

                opNamesTextBox.Text = displayText;
                opNamesTextBox.FontStyle = fontStyle;
            }));
        }



        private void ExecuteActionBasedOnTrigger(string opName, int triggerValue)
        {

            try
            {
                // Replace with your actual logic to determine and call the method
                // For example, if triggerValue == 1, call the login method
                if (triggerValue == 1)
                {
                    // Write "processing" status to CIMPLE.Status
                    var statusTag = tagStore.GetString($"{opName}.Status");
                    statusTag = tagStore.GetString($"{opName}.Status");

                    var stationTag = tagStore.GetString($"{opName}.Station");
                    var userTag = tagStore.GetString($"{opName}.Login.User");
                    var passwordTag = tagStore.GetString($"{opName}.Login.Password");

                    // Perform login operation
                    string station = tagStore.ReadString(stationTag);
                    string user = tagStore.ReadString(userTag);
                    string password = tagStore.ReadString(passwordTag); ;
                    MESIntegration mesIntegration = new MESIntegration();
                    var loginResult = mesIntegration.Login(station, user, password);
                    string resultString = loginResult.ToString(); // Assuming loginResult can be converted to string

                    // Write the login result to CIMPLE.response
                    var responseTag = tagStore.GetStringArray($"{opName}.Response", 1); // Assuming array length is 1
                    tagStore.WriteStringArray(responseTag, new string[] { resultString });

                    // Update status to "done"
                    tagStore.WriteString(statusTag, "done");


                    try
                    {
                        Log(opName, $"Login method called with station: {station}, user: {user}, response: {resultString}\n\n");
                    }
                    catch (Exception ex)
                    {
                        // Log the exception
                        Console.WriteLine($"Error in dialogbox method 1: {ex.Message}");
                        // Optionally, display the error in the UI

                    }

                    Console.WriteLine($"Login method called with station: {station}, user: {user}, response: {resultString}\n\n");



                }

                if (triggerValue == 2 && !string.IsNullOrEmpty(plcIpAddress))
                {
                    try
                    {
                        var stationTag = tagStore.GetString($"{opName}.Station");
                        string station = tagStore.ReadString(stationTag); ;

                        MESIntegration mesIntegration = new MESIntegration();
                        var result = mesIntegration.WorkOrder_GetActiveByStation(station); // Replace with actual parameters

                        int errorCode = result.ErrorCode;
                        string errorDescription = result.ErrorDescription;
                        string partNumber = result.PartNumber ?? "N/A"; // Using ?? to handle nulls

                        // Log the results along with the station name
                        Dispatcher.Invoke(() => Log(opName, $"Invoking method WorkOrder_GetActiveByStation for station: {station}, with ErrorCode: {errorCode}, ErrorDescription: {errorDescription}, PartNumber: {partNumber}\n\n"));

                        // Write the results to CIMPLE.response array
                        var responseTag = tagStore.GetStringArray($"{opName}.Response", 3); // Array length is 3

                        if (string.IsNullOrEmpty(errorDescription))
                        {
                            // If ErrorDescription is empty or null, provide a default value
                            errorDescription = "No error description available";
                        }

                        if (errorDescription.Length > 80)
                        {
                            errorDescription = errorDescription.Substring(0, 80);
                        }

                        tagStore.WriteStringArray(responseTag, new string[] { errorCode.ToString(), errorDescription, partNumber });

                        // Update CIMPLE.Status to "done"
                        var statusTag = tagStore.GetString($"{opName}.Status");
                        tagStore.WriteString(statusTag, "done");

                    }
                    catch (Exception ex)
                    {
                        // Log the exception
                        Console.WriteLine($"Error in method 2: {ex.Message}");
                        // Optionally, display the error in the UI
                        Dispatcher.Invoke(() => Log(opName, $"Error in method 2: {ex.Message}\n"));
                    }
                }


                if (triggerValue == 3 && !string.IsNullOrEmpty(plcIpAddress))
                {
                    // Initialize necessary tags from the PLC
                    var stationTag = tagStore.GetString($"{opName}.Station");
                    var serialNumberTag = tagStore.GetString($"{opName}.Serial_AssignToWorkOrder.SerialNumber");
                    var positionTag = tagStore.GetDint($"{opName}.Serial_AssignToWorkOrder.Position");

                    // Read values from PLC tags
                    string station = tagStore.ReadString(stationTag);
                    string serialNumber = tagStore.ReadString(serialNumberTag);
                    int position = tagStore.ReadDint(positionTag);

                    MESIntegration mesIntegration = new MESIntegration();
                    var result = mesIntegration.Serial_AssignToWorkOrder(station, serialNumber, position);

                    // Extract ErrorCode and ErrorDescription from the result
                    int errorCode = result.ErrorCode;
                    string errorDescription = result.ErrorDescription;

                    // Log the result in the dialog box
                    Dispatcher.Invoke(() => Log(opName, $"Invoking method Serial_AssignToWorkOrder for station: {station} with SerialNumber: {serialNumber} and Position: {position} returned ErrorCode: {errorCode}, ErrorDescription: {errorDescription}\n\n"));

                    // Write the results to CIMPLE.response array
                    var responseTag = tagStore.GetStringArray($"{opName}.Response", 2); // Array length is 3

                    if (string.IsNullOrEmpty(errorDescription))
                    {
                        // If ErrorDescription is empty or null, provide a default value
                        errorDescription = "No error description available";
                    }

                    if (errorDescription.Length > 80)
                    {
                        errorDescription = errorDescription.Substring(0, 80);
                    }

                    tagStore.WriteStringArray(responseTag, new string[] { errorCode.ToString(), errorDescription });

                    // Update CIMPLE.Status to "done"
                    var statusTag = tagStore.GetString($"{opName}.Status");
                    tagStore.WriteString(statusTag, "done");

                }


                if (triggerValue == 4 && !string.IsNullOrEmpty(plcIpAddress))
                {
                    // Initialize necessary tags from the PLC
                    var stationTag = tagStore.GetString($"{opName}.Station");
                    var serialNumberTag = tagStore.GetString($"{opName}.Serial_MoveIn.SerialNumber");
                    var activateWorkOrderTag = tagStore.GetBool($"{opName}.Serial_MoveIn.ActivateWorkOrder");
                    var layerTag = tagStore.GetDint($"{opName}.Serial_MoveIn.Layer");

                    // Write "processing" status to CIMPLE.Status
                    var statusTag = tagStore.GetString($"{opName}.Status");
                    statusTag = tagStore.GetString($"{opName}.Status");

                    // Read values from PLC tags
                    string station = tagStore.ReadString(stationTag);
                    string serialNumber = tagStore.ReadString(serialNumberTag);
                    bool activateWorkOrder = tagStore.ReadBool(activateWorkOrderTag);
                    int layer = tagStore.ReadDint(layerTag);

                    MESIntegration mesIntegration = new MESIntegration();
                    var result = mesIntegration.Serial_MoveIn(station, serialNumber, activateWorkOrder, layer);

                    // Extracting ErrorCode and ErrorDescription from the result
                    int errorCode = result.ErrorCode;
                    string errorDescription = result.ErrorDescription;

                    // Log the result in the dialog box
                    Dispatcher.Invoke(() => Log(opName, $"Invoking method Serial_MoveIn for station: {station} with SerialNumber: {serialNumber}, ativateWorkOrder: {activateWorkOrder} and layer: {layer} returned ErrorCode: {errorCode}, ErrorDescription: {errorDescription}\n"));
                    Console.WriteLine($"Invoking method Serial_MoveIn for station: {station} with SerialNumber: {serialNumber}, ativateWorkOrder: {activateWorkOrder} and layer: {layer} returned ErrorCode: {errorCode}, ErrorDescription: {errorDescription}\n");

                    // Write the results to CIMPLE.response array
                    var responseTag = tagStore.GetStringArray($"{opName}.Response", 2); // Array length is 3

                    if (string.IsNullOrEmpty(errorDescription))
                    {
                        // If ErrorDescription is empty or null, provide a default value
                        errorDescription = "No error description available";
                    }

                    if (errorDescription.Length > 80)
                    {
                        errorDescription = errorDescription.Substring(0, 80);
                    }

                    tagStore.WriteStringArray(responseTag, new string[] { errorCode.ToString(), errorDescription });

                    // Update CIMPLE.Status to "done"
                    tagStore.WriteString(statusTag, "done");

                }

                // Example usage of the ReadStringArray method
                if (triggerValue == 5 && !string.IsNullOrEmpty(plcIpAddress))
                {
                    // Initialize necessary tags from the PLC
                    var stationTag = tagStore.GetString($"{opName}.Station");
                    var serialNumberTag = tagStore.GetString($"{opName}.Serial_SetAttribute.SerialNumber");

                    // Read values from PLC tags
                    string station = tagStore.ReadString(stationTag);
                    string serialNumber = tagStore.ReadString(serialNumberTag);
                    var infoIdsTag = tagStore.GetStringArray($"{opName}.Serial_SetAttribute.InfoId", 2);
                    string[] infoIds = tagStore.ReadStringArray(infoIdsTag);

                    var infoValuesTag = tagStore.GetStringArray($"{opName}.Serial_SetAttribute.InfoValue", 2);
                    string[] infoValues = tagStore.ReadStringArray(infoValuesTag);

                    // Check if arrays have the same length
                    if (infoIds.Length != infoValues.Length)
                    {
                        Console.WriteLine("Error: InfoId and InfoValue arrays have different lengths.");
                        return;
                    }

                    // Create UnitInfo objects
                    List<UnitInfo> unitInfoList = new List<UnitInfo>();
                    for (int i = 0; i < infoIds.Length; i++)
                    {
                        unitInfoList.Add(new UnitInfo { InfoId = infoIds[i], InfoValue = infoValues[i] });

                        //MessageBox.Show(infoIds[i] + infoValues[i]);
                    }



                    // Call the method with the UnitInfo list
                    MESIntegration mesIntegration = new MESIntegration();
                    var result = mesIntegration.Serial_SetAttribute(station, serialNumber, unitInfoList);

                    // Extracting ErrorCode and ErrorDescription from the result
                    int errorCode = result.ErrorCode;
                    string errorDescription = result.ErrorDescription;

                    // Log the result in the dialog box
                    Dispatcher.Invoke(() => Log(opName, $"Invoking method Serial_SetAttribute for station: {station} with SerialNumber: {serialNumber} returned ErrorCode: {errorCode}, ErrorDescription: {errorDescription}\n\n"));

                    // Write the results to CIMPLE.response array
                    var responseTag = tagStore.GetStringArray($"{opName}.Response", 2); // Array length is 2

                    if (string.IsNullOrEmpty(errorDescription))
                    {
                        // If ErrorDescription is empty or null, provide a default value
                        errorDescription = "No error description available";
                    }

                    if (errorDescription.Length > 80)
                    {
                        errorDescription = errorDescription.Substring(0, 80);
                    }

                    tagStore.WriteStringArray(responseTag, new string[] { errorCode.ToString(), errorDescription });

                    // Update CIMPLE.Status to "done"
                    var statusTag = tagStore.GetString($"{opName}.Status");

                    tagStore.WriteString(statusTag, "done");

                }


                if (triggerValue == 6 && !string.IsNullOrEmpty(plcIpAddress))
                {

                    // Initialize necessary tags from the PLC
                    var stationTag = tagStore.GetString($"{opName}.Station");
                    var serialNumberTag = tagStore.GetString($"{opName}.Serial_MoveOut.SerialNumber");
                    var resultsTag = tagStore.GetDint($"{opName}.Serial_MoveOut.Results");
                    var layerTag = tagStore.GetDint($"{opName}.Serial_MoveOut.Layer");
                    var checkMultiBoardTag = tagStore.GetBool($"{opName}.Serial_MoveOut.CheckMultiBoard");

                    // Read values from PLC tags
                    string station = tagStore.ReadString(stationTag);
                    string serialNumber = tagStore.ReadString(serialNumberTag);
                    int resultInt = tagStore.ReadDint(resultsTag);
                    int layer = tagStore.ReadDint(layerTag);
                    bool checkMultiBoard = tagStore.ReadBool(checkMultiBoardTag);

                    Results result;

                    switch (resultInt)
                    {
                        case 0:
                            result = Results.PA;
                            break;
                        case 1:
                            result = Results.FA;
                            break;
                        default:
                            result = Results.SC; // Default to SC if the value doesn't match any enum
                            break;
                    }

                    MESIntegration mesIntegration = new MESIntegration();
                    var response = mesIntegration.Serial_MoveOut(station, serialNumber, (Enums.Results)resultInt, layer, checkMultiBoard);

                    // Extracting ErrorCode and ErrorDescription from the result
                    int errorCode = response.ErrorCode;
                    string errorDescription = response.ErrorDescription;

                    // Log the result in the dialog box
                    Dispatcher.Invoke(() => Log(opName, $"Invoking method Serial_MoveOut for station: {station} with SerialNumber: {serialNumber}, result: {resultInt}, layer: {layer} and checkMultiBoard: {checkMultiBoard} returned ErrorCode: {errorCode}, ErrorDescription: {errorDescription}\n\n"));

                    // Write the results to CIMPLE.response array
                    var responseTag = tagStore.GetStringArray($"{opName}.Response", 2); // Array length is 3

                    if (string.IsNullOrEmpty(errorDescription))
                    {
                        // If ErrorDescription is empty or null, provide a default value
                        errorDescription = "No error description available";
                    }

                    if (errorDescription.Length > 80)
                    {
                        errorDescription = errorDescription.Substring(0, 80);
                    }

                    tagStore.WriteStringArray(responseTag, new string[] { errorCode.ToString(), errorDescription });

                    // Update CIMPLE.Status to "done"
                    var statusTag = tagStore.GetString($"{opName}.Status");
                    tagStore.WriteString(statusTag, "done");

                }

                if (triggerValue == 7 && !string.IsNullOrEmpty(plcIpAddress))
                {
                    // Initialize tags for reading station and serial number
                    var stationTag = tagStore.GetString($"{opName}.Station");
                    var serialNumberTag = tagStore.GetString($"{opName}.Serial_GetInformation.SerialNumber");

                    // Read station and serial number from PLC
                    string station = tagStore.ReadString(stationTag);
                    string serialNumber = tagStore.ReadString(serialNumberTag);

                    // Call Serial_GetInformation method
                    MESIntegration mesIntegration = new MESIntegration();
                    var result = mesIntegration.Serial_GetInformation(station, serialNumber);

                    // Extracting ErrorCode and ErrorDescription from the result
                    int errorCode = result.ErrorCode;
                    string errorDescription = result.ErrorDescription;


                    // Log the result in the dialog box
                    Dispatcher.Invoke(() => Log(opName, $"Invoking method Serial_GetInformation for station: {station} with SerialNumber: {serialNumber} returned ErrorCode: {errorCode.ToString()}, ErrorDescription: {errorDescription}\n\n"));


                    //MessageBox.Show(errorCode.ToString());
                    //MessageBox.Show(errorDescription);
                    //MessageBox.Show(serialNumber);
                    //MessageBox.Show(opName);

                    // Write the results to CIMPLE.response array
                    var responseTag = tagStore.GetStringArray($"{opName}.Response", 2); // Array length is 2

                    //responseTag.Value = new string[] { errorCode.ToString(), errorDescription };
                    //responseTag.Write();

                    if (string.IsNullOrEmpty(errorDescription))
                    {
                        // If ErrorDescription is empty or null, provide a default value
                        errorDescription = "No error description";
                    }

                    //errorDescription = "No error description";

                    if (errorDescription.Length > 80)
                    {
                        errorDescription = errorDescription.Substring(0, 80);
                    }



                    //try
                    //{

                    tagStore.WriteStringArray(responseTag, new string[] { errorCode.ToString(), errorDescription });

                    //}
                    //catch (Exception ex)
                    //{
                    //    MessageBox.Show(ex.Message);
                    //}


                    // Update CIMPLE.Status to "done"
                    var statusTag = tagStore.GetString($"{opName}.Status");

                    tagStore.WriteString(statusTag, "done");


                }


                //AA@2024/07/15 - Versão com os mesauredata num array 
                // Assuming triggerValue, plcIpAddress, and necessary setup are already defined
                if (triggerValue == 8 && !string.IsNullOrEmpty(plcIpAddress))
                {
                    try
                    {
                        // Additional Parameter LenghtMeasureData
                        int LenghtMeasureData = tagStore.ReadDint(tagStore.GetDint($"{opName}.Serial_MoveOutAndTestResults.LenghtMeasureData"));
                        if (LenghtMeasureData < 0)
                            LenghtMeasureData = 0;
                        if (LenghtMeasureData > MaxMeasurementDataItems)
                        {
                            Log(opName, $"MeasureData length {LenghtMeasureData} is above safety limit {MaxMeasurementDataItems}; processing only the first {MaxMeasurementDataItems}.\n");
                            LenghtMeasureData = MaxMeasurementDataItems;
                        }

                        // Initialize and read necessary tags from the PLC for identification
                        string station = tagStore.ReadString(tagStore.GetString($"{opName}.Station"));
                        string serialNumber = tagStore.ReadString(tagStore.GetString($"{opName}.Serial_MoveOutAndTestResults.SerialNumber"));
                        // Additional details for processing (groupId, groupVersion, etc.)
                        string groupId = tagStore.ReadString(tagStore.GetString($"{opName}.Serial_MoveOutAndTestResults.GroupId"));
                        string groupVersion = tagStore.ReadString(tagStore.GetString($"{opName}.Serial_MoveOutAndTestResults.GroupVersion"));
                        int resultInt = tagStore.ReadDint(tagStore.GetDint($"{opName}.Serial_MoveOutAndTestResults.Results"));
                        int layer = tagStore.ReadDint(tagStore.GetDint($"{opName}.Serial_MoveOutAndTestResults.Layer"));
                        bool checkMultiBoard = tagStore.ReadBool(tagStore.GetBool($"{opName}.Serial_MoveOutAndTestResults.CheckMultiBoard"));
                        Enums.Results result;

                        switch (resultInt)
                        {
                            case 0:
                                result = Enums.Results.Fail;
                                break;
                            case 1:
                                result = Enums.Results.Pass;
                                break;
                            case 2:
                                result = Enums.Results.Scrap;
                                break;
                            default:
                                result = Enums.Results.NA; // Default to NA if the value doesn't match any enum
                                break;
                        }

                        int i = 0;

                        List<string> destinationList = new List<string>();

                        // Dynamically constructing Measure objects from the measurementData array
                        List<MES_HAI.Entity.Measure> measuresList = new List<MES_HAI.Entity.Measure>();

                        while (i < LenghtMeasureData)

                        {
                            /*MeasureData All Parameters
                                HighLimit = parts[0],
                                LowLimit = parts[1],
                                MeasureKey = parts[2],
                                MeasureNotes = parts[3],
                                MeasureValue = parts[4],
                                Position = int.Parse(parts[5]),
                                Result = parts[6], // Assigning the parsed enum value
                                Tolerance = parts[7],
                                UnitOfMeasure = parts[8]
                             */


                            var highlimit = tagStore.GetString($"{opName}.Serial_MoveOutAndTestResults.MeasureData[{i}].HighLimit");
                            var highL = tagStore.ReadString(highlimit);
                            var Lowlimit = tagStore.GetString($"{opName}.Serial_MoveOutAndTestResults.MeasureData[{i}].LowLimit");
                            var LowL = tagStore.ReadString(Lowlimit);
                            var MeasureKey = tagStore.GetString($"{opName}.Serial_MoveOutAndTestResults.MeasureData[{i}].MeasureKey");
                            var MeasureK = tagStore.ReadString(MeasureKey);
                            var MeasureNotes = tagStore.GetString($"{opName}.Serial_MoveOutAndTestResults.MeasureData[{i}].MeasureNotes");
                            var MeasureN = tagStore.ReadString(MeasureNotes);
                            var MeasureValue = tagStore.GetString($"{opName}.Serial_MoveOutAndTestResults.MeasureData[{i}].MeasureValue");
                            var MeasureV = tagStore.ReadString(MeasureValue);
                            var Position = tagStore.GetString($"{opName}.Serial_MoveOutAndTestResults.MeasureData[{i}].Position");
                            var Pos = tagStore.ReadString(Position);
                            var Result = tagStore.GetString($"{opName}.Serial_MoveOutAndTestResults.MeasureData[{i}].Result");
                            var Res = tagStore.ReadString(Result);
                            var Tolerance = tagStore.GetString($"{opName}.Serial_MoveOutAndTestResults.MeasureData[{i}].Tolerance");
                            var Tol = tagStore.ReadString(Tolerance);
                            var UnitOfMeasure = tagStore.GetString($"{opName}.Serial_MoveOutAndTestResults.MeasureData[{i}].UnitOfMeasure");
                            var UOM = tagStore.ReadString(UnitOfMeasure);

                            // Parse the measure result from parts of the data, assuming it's at a specific index, e.g., parts[6]
                            int measureResultInt = int.Parse(Res); // Adjust the index based on your actual data structure
                            Enums.MeasureResults Mresult;
                            switch (measureResultInt)
                            {
                                case 0:
                                    Mresult = Enums.MeasureResults.Fail;
                                    break;
                                case 1:
                                    Mresult = Enums.MeasureResults.Pass;
                                    break;
                                case 2:
                                    Mresult = Enums.MeasureResults.False;
                                    break;
                                case 3:
                                    Mresult = Enums.MeasureResults.Retouch;
                                    break;
                                default:
                                    Mresult = Enums.MeasureResults.Fail; // Default to Fail if the value doesn't match any known enum
                                    break;
                            }

                            var measure = new MES_HAI.Entity.Measure
                            {
                                HighLimit = highL,
                                LowLimit = LowL,
                                MeasureKey = MeasureK,
                                MeasureNotes = MeasureN,
                                MeasureValue = MeasureV,
                                Position = int.Parse(Pos),
                                Result = Mresult, // Assigning the parsed enum value
                                Tolerance = Tol,
                                UnitOfMeasure = UOM
                            };

                            measuresList.Add(measure);

                            i++;

                        }

                        // Pass the measures list to the MES system
                        MESIntegration mesIntegration = new MESIntegration();
                        ErrorDetail errorDetail = mesIntegration.Serial_MoveOutAndTestResults(
                            station, serialNumber, result, groupId, groupVersion, measuresList.ToArray(), layer, checkMultiBoard);

                        // After constructing the measuresList
                        StringBuilder measurementDataString = new StringBuilder();
                        foreach (var measure in measuresList)
                        {
                            measurementDataString.AppendFormat("HighLimit: {0}, LowLimit: {1}, MeasureKey: {2}, MeasureNotes: {3}, MeasureValue: {4}, Position: {5}, Result: {6}, Tolerance: {7}, UnitOfMeasure: {8};\n", measure.HighLimit, measure.LowLimit, measure.MeasureKey, measure.MeasureNotes, measure.MeasureValue, measure.Position, measure.Result.ToString(), measure.Tolerance, measure.UnitOfMeasure);
                        }

                        // Outputting the entire process for logging or debugging, with measurements details before error code and error description
                        Dispatcher.Invoke(() => Log(opName, $"Invoking method Serial_MoveOutAndTestResults for station: {station} with SerialNumber: {serialNumber}, result: {result}, layer: {layer}, checkMultiBoard: {checkMultiBoard}. Measurement Data: {measurementDataString.ToString()} Returned ErrorCode: {errorDetail.ErrorCode}, ErrorDescription: {errorDetail.ErrorDescription}\n\n"));

                        // Optionally, write the results back to the PLC or perform other finalization actions
                        var responseTag = tagStore.GetStringArray($"{opName}.Response", 2);
                        tagStore.WriteStringArray(responseTag, new string[] { errorDetail.ErrorCode.ToString(), errorDetail.ErrorDescription });

                        var statusTag = tagStore.GetString($"{opName}.Status");
                        tagStore.WriteString(statusTag, "done");



                    }
                    catch (Exception ex)
                    {
                        // Log the exception
                        Console.WriteLine($"Error in method 8: {ex.Message}");
                        // Optionally, display the error in the UI
                        Dispatcher.Invoke(() => Log(opName, $"Error in method 8: {ex.Message}\n"));
                    }
                }

                if (triggerValue == 9 && !string.IsNullOrEmpty(plcIpAddress))
                {
                    // Initialize necessary tags from the PLC
                    var stationTag = tagStore.GetString($"{opName}.Station");
                    var serialNumberTag = tagStore.GetString($"{opName}.Serial_GetAttributeValues.SerialNumber");
                    var attributeKeyTag = tagStore.GetString($"{opName}.Serial_GetAttributeValues.AttributeKey");

                    // Read values from PLC tags
                    string station = tagStore.ReadString(stationTag);
                    string serialNumber = tagStore.ReadString(serialNumberTag);
                    string attributeKey = tagStore.ReadString(attributeKeyTag);

                    MESIntegration mesIntegration = new MESIntegration();
                    var result = mesIntegration.Serial_GetAttributeValues(station, serialNumber, attributeKey);

                    // Extracting ErrorCode and ErrorDescription from the result tuple
                    int errorCode = result.attributes.ErrorCode;
                    string errorDescription = result.attributes.ErrorDescription;

                    // Log the result in the dialog box
                    Dispatcher.Invoke(() => Log(opName, $"Invoking method Serial_GetAttributeValues for station: {station} with SerialNumber: {serialNumber} and AttributeKey: {attributeKey} returned ErrorCode: {errorCode}, ErrorDescription: {errorDescription}\n\n"));

                    // Write the results to CIMPLE.response array
                    var responseTag = tagStore.GetStringArray($"{opName}.Response", 2); // Array length is 2

                    if (string.IsNullOrEmpty(errorDescription))
                    {
                        // If ErrorDescription is empty or null, provide a default value
                        errorDescription = "No error description available";
                    }

                    if (errorDescription.Length > 80)
                    {
                        errorDescription = errorDescription.Substring(0, 80);
                    }

                    tagStore.WriteStringArray(responseTag, new string[] { errorCode.ToString(), errorDescription });

                    // Update CIMPLE.Status to "done"
                    var statusTag = tagStore.GetString($"{opName}.Status");
                    tagStore.WriteString(statusTag, "done");

                }

                if (triggerValue == 10 && !string.IsNullOrEmpty(plcIpAddress))
                {
                    // Initialize necessary tags from the PLC
                    var stationTag = tagStore.GetString($"{opName}.Station");
                    var childSerialNumberTag = tagStore.GetString($"{opName}.Serial_VerifyMergeNoMoveIn.ChildSerialNumber");

                    // Read values from PLC tags
                    string station = tagStore.ReadString(stationTag);
                    string childSerialNumber = tagStore.ReadString(childSerialNumberTag);

                    MESIntegration mesIntegration = new MESIntegration();
                    var result = mesIntegration.Serial_VerifyMergeNoMoveIn(station, childSerialNumber);

                    // Extracting ErrorCode and ErrorDescription from the result
                    int errorCode = result.ErrorCode;
                    string errorDescription = result.ErrorDescription;

                    // Log the result in the dialog box
                    Dispatcher.Invoke(() => Log(opName, $"Invoking method Serial_VerifyMergeNoMoveIn for station: {station} with ChildSerialNumber: {childSerialNumber} returned ErrorCode: {errorCode}, ErrorDescription: {errorDescription}\n\n"));

                    // Write the results to CIMPLE.response array
                    var responseTag = tagStore.GetStringArray($"{opName}.Response", 2); // Array length is 2

                    if (string.IsNullOrEmpty(errorDescription))
                    {
                        // If ErrorDescription is empty or null, provide a default value
                        errorDescription = "No error description available";
                    }

                    if (errorDescription.Length > 80)
                    {
                        errorDescription = errorDescription.Substring(0, 80);
                    }

                    tagStore.WriteStringArray(responseTag, new string[] { errorCode.ToString(), errorDescription });

                    // Update CIMPLE.Status to "done"
                    var statusTag = tagStore.GetString($"{opName}.Status");
                    tagStore.WriteString(statusTag, "done");

                }

                if (triggerValue == 11 && !string.IsNullOrEmpty(plcIpAddress))
                {

                    try
                    {


                        // Initialize necessary tags from the PLC
                        var stationTag = tagStore.GetString($"{opName}.Station");
                        var parentSerialNumberTag = tagStore.GetString($"{opName}.Serial_Correlation.ParentSerialNumber");
                        var Child1SerialTag = tagStore.GetString($"{opName}.Serial_Correlation.Child1");
                        var Child2SerialTag = tagStore.GetString($"{opName}.Serial_Correlation.Child2");

                        // Read values from PLC tags
                        string station = tagStore.ReadString(stationTag);
                        string parentSerialNumber = tagStore.ReadString(parentSerialNumberTag);
                        string Child1Serial = tagStore.ReadString(Child1SerialTag);
                        string Child2Serial = tagStore.ReadString(Child2SerialTag);

                        // Initialize list with values read from PLC
                        List<string> children = new List<string>();
                        if (!string.IsNullOrEmpty(Child1Serial))
                        {
                            children.Add(Child1Serial);
                        }
                        if (!string.IsNullOrEmpty(Child2Serial))
                        {
                            children.Add(Child2Serial);
                        }

                        MESIntegration mesIntegration = new MESIntegration();
                        var result = mesIntegration.Serial_Correlation(station, children, parentSerialNumber);



                        // Extracting ErrorCode and ErrorDescription from the result
                        int errorCode = result.ErrorCode;
                        string errorDescription = result.ErrorDescription;

                        // Log the result in the dialog box
                        Dispatcher.Invoke(() => Log(opName, $"Invoking method Serial_Correlation for station: {station}, ParentSerialNumber: {parentSerialNumber}, and Children: [{string.Join(", ", children)}] returned ErrorCode: {errorCode}, ErrorDescription: {errorDescription}\n\n"));

                        // Write the results to CIMPLE.response array
                        var responseTag = tagStore.GetStringArray($"{opName}.Response", 2); // Array length is 2

                        if (string.IsNullOrEmpty(errorDescription))
                        {
                            // If ErrorDescription is empty or null, provide a default value
                            errorDescription = "No error description available";
                        }

                        if (errorDescription.Length > 80)
                        {
                            errorDescription = errorDescription.Substring(0, 80);
                        }

                        tagStore.WriteStringArray(responseTag, new string[] { errorCode.ToString(), errorDescription });

                        // Update CIMPLE.Status to "done"
                        var statusTag = tagStore.GetString($"{opName}.Status");
                        tagStore.WriteString(statusTag, "done");


                    }
                    catch (Exception ex)
                    {
                        // Log the exception
                        Console.WriteLine($"Error in method Correlation: {ex.Message}");
                        // Optionally, display the error in the UI
                        Dispatcher.Invoke(() => Log(opName, $"Error in method Serial_Correlation: {ex.Message}\n"));
                    }
                }

                //new Panorama methods
                if (triggerValue == 12 && !string.IsNullOrEmpty(plcIpAddress))
                {
                    // Initialize necessary tags from the PLC
                    var stationTag = tagStore.GetString($"{opName}.Station");

                    // Initialize the tag to read part name and compare
                    var name = tagStore.GetString($"{opName}.WorkOrder_ListByStation.PartName");

                    // Update status to "Processing"
                    var statusTag = tagStore.GetString($"{opName}.Status");

                    // Read value from PLC tag
                    string station = tagStore.ReadString(stationTag);

                    // Read value from PLC tag
                    string partName = tagStore.ReadString(name);

                    MESIntegration mesIntegration = new MESIntegration();
                    var result = mesIntegration.WorkOrder_ListByStation(station);

                    // Extracting ErrorCode and ErrorDescription from the result
                    int errorCode = result.ErrorCode;
                    string errorDescription = result.ErrorDescription;
                    List<WorkOrder> workorders = result.List; // Assuming List contains WorkOrder objects

                    // Log the result in the dialog box
                    Dispatcher.Invoke(() => Log(opName, $"Invoking method WorkOrder_ListByStation for station: {station} returned ErrorCode: {errorCode}, ErrorDescription: {errorDescription}\n\n"));

                    string workOrdersResponse = "";
                    string verify = "NF";

                    // Convert work orders to list of strings with ";" as a delimiter
                    List<string> workOrderStrings = new List<string>();
                    if (workorders != null)
                    {
                        foreach (var workOrder in workorders)
                        {
                            // Construct the string with delimiter
                            string workOrderString = $"{workOrder.Name}";
                            workOrdersResponse = workOrdersResponse + $" {workOrder.Name}";
                            workOrderStrings.Add(workOrderString);

                            if (partName == workOrder.Name)
                            {
                                verify = workOrder.Name;
                            }
                        }
                    }

                    // Append the work orders to the dialog box log
                    if (workOrderStrings.Count > 0)
                    {
                        //dialogBox.AppendText("WorkOrders: ");

                        foreach (var workOrderString in workOrderStrings)
                        {

                            // dialogBox.AppendText($"{workOrderString},");
                        }
                    }
                    else
                    {
                        //Console.WriteLine("No work orders found.\n");
                        Log(opName, "No work orders found.\n");
                    }

                    // Write the results to the response array
                    //int responseLength = 2 + workOrderStrings.Count; // 2 for errorCode and errorDescription + workorders count
                    var responseTag = tagStore.GetStringArray($"{opName}.Response", 3); // Adjust array length

                    if (string.IsNullOrEmpty(errorDescription))
                    {
                        // If ErrorDescription is empty or null, provide a default value
                        errorDescription = "No error description available";
                    }

                    if (errorDescription.Length > 80)
                    {
                        errorDescription = errorDescription.Substring(0, 80);
                    }

                    tagStore.WriteStringArray(responseTag, new string[] { errorCode.ToString(), errorDescription, verify });

                    // Update status to "done"
                    tagStore.WriteString(statusTag, "done");

                }

                if (triggerValue == 13 && !string.IsNullOrEmpty(plcIpAddress))
                {
                    // Initialize necessary tags from the PLC
                    var stationTag = tagStore.GetString($"{opName}.Station");
                    var workOrderNameTag = tagStore.GetString($"{opName}.WorkOrder_Activate.WorkOrder");

                    // Read values from PLC tags
                    string station = tagStore.ReadString(stationTag);
                    string workOrderName = tagStore.ReadString(workOrderNameTag);

                    MESIntegration mesIntegration = new MESIntegration();
                    var result = mesIntegration.WorkOrder_Activate(station, workOrderName);

                    // Extracting ErrorCode and ErrorDescription from the result
                    int errorCode = result.ErrorCode;
                    string errorDescription = result.ErrorDescription;

                    // Log the result in the dialog box
                    Dispatcher.Invoke(() => Log(opName, $"Invoking method WorkOrder_Activate for station: {station}, WorkOrderName: {workOrderName} returned ErrorCode: {errorCode}, ErrorDescription: {errorDescription}\n\n"));

                    // Write the results to the response array
                    var responseTag = tagStore.GetStringArray($"{opName}.Response", 2); // Array length is 2

                    if (string.IsNullOrEmpty(errorDescription))
                    {
                        // If ErrorDescription is empty or null, provide a default value
                        errorDescription = "No error description available";
                    }

                    if (errorDescription.Length > 80)
                    {
                        errorDescription = errorDescription.Substring(0, 80);
                    }

                    tagStore.WriteStringArray(responseTag, new string[] { errorCode.ToString(), errorDescription });

                    // Update status to "done"
                    var statusTag = tagStore.GetString($"{opName}.Status");
                    tagStore.WriteString(statusTag, "done");

                }

                // Inside the trigger method
                if (triggerValue == 14 && !string.IsNullOrEmpty(plcIpAddress))
                {
                    try
                    {
                        // Initialize necessary tags from the PLC
                        var stationTag = tagStore.GetString($"{opName}.Station");
                        var objectNameTag = tagStore.GetString($"{opName}.Serial_ListNextByQuantity.ObjectName");
                        var quantityTag = tagStore.GetDint($"{opName}.Serial_ListNextByQuantity.Quantity");

                        // Read values from PLC tags
                        string station = tagStore.ReadString(stationTag);
                        string objectName = tagStore.ReadString(objectNameTag);
                        int quantity = tagStore.ReadDint(quantityTag);

                        // Invoke the Serial_ListNextByQuantity method
                        MESIntegration mesIntegration = new MESIntegration();
                        var result = mesIntegration.Serial_ListNextByQuantity(station, objectName, quantity);

                        // Extracting ErrorCode and ErrorDescription from the result
                        int errorCode = result.ErrorCode;
                        string errorDescription = result.ErrorDescription;

                        // Log the result in the dialog box
                        Dispatcher.Invoke(() => Log(opName, $"Invoking method Serial_ListNextByQuantity for station: {station}, ObjectName: {objectName}, Quantity: {quantity} returned ErrorCode: {errorCode}, ErrorDescription: {errorDescription}\n\n"));

                        // Write the results to CIMPLE.response array
                        var responseTag = tagStore.GetStringArray($"{opName}.Response", 2);

                        if (string.IsNullOrEmpty(errorDescription))
                        {
                            // If ErrorDescription is empty or null, provide a default value
                            errorDescription = "No error description available";
                        }

                        if (errorDescription.Length > 80)
                        {
                            errorDescription = errorDescription.Substring(0, 80);
                        }

                        tagStore.WriteStringArray(responseTag, new string[] { errorCode.ToString(), errorDescription });

                        // Update CIMPLE.Status to "done"
                        var statusTag = tagStore.GetString($"{opName}.Status");
                        tagStore.WriteString(statusTag, "done");

                    }
                    catch (Exception ex)
                    {
                        // Log the exception
                        Console.WriteLine($"Error in method Serial_ListNextByQuantity: {ex.Message}");
                        // Optionally, display the error in the UI
                        Dispatcher.Invoke(() => Log(opName, $"Error in method Serial_ListNextByQuantity: {ex.Message}\n"));
                    }
                }

                // Inside the trigger method
                if (triggerValue == 15 && !string.IsNullOrEmpty(plcIpAddress))
                {
                    // Initialize necessary tags from the PLC
                    var stationTag = tagStore.GetString($"{opName}.Station");
                    var childSerialNumberTag = tagStore.GetString($"{opName}.Serial_VerifyMerge.ChildSerialNumber");

                    // Read values from PLC tags
                    string station = tagStore.ReadString(stationTag);
                    string childSerialNumber = tagStore.ReadString(childSerialNumberTag);

                    // Invoke the Serial_ListNextByQuantity method
                    MESIntegration mesIntegration = new MESIntegration();
                    var result = mesIntegration.Serial_VerifyMerge(station, childSerialNumber);

                    // Log the result in the dialog box
                    Dispatcher.Invoke(() => Log(opName, $"Invoking method Serial_VerifyMerge for station: {station}, ChildSerialNumber: {childSerialNumber} returned ErrorCode: {result.ErrorCode}, ErrorDescription: {result.ErrorDescription}\n\n"));

                    // Write the results to CIMPLE.response array
                    var responseTag = tagStore.GetStringArray($"{opName}.Response", 2); // Array length is 2

                    if (string.IsNullOrEmpty(result.ErrorDescription))
                    {
                        // If ErrorDescription is empty or null, provide a default value
                        result.ErrorDescription = "No error description available";
                    }

                    tagStore.WriteStringArray(responseTag, new string[] { result.ErrorCode.ToString(), result.ErrorDescription });

                    // Update CIMPLE.Status to "done"
                    var statusTag = tagStore.GetString($"{opName}.Status");
                    tagStore.WriteString(statusTag, "done");

                }


                if (triggerValue == 16 && !string.IsNullOrEmpty(plcIpAddress))
                {
                    // Initialize necessary tags from the PLC
                    var stationTag = tagStore.GetString($"{opName}.Station");
                    var serialNumberTag = tagStore.GetString($"{opName}.Serial_GetPartTrackingAnnounceSerial.SerialNumber");

                    // Read values from PLC tags
                    string station = tagStore.ReadString(stationTag);
                    string serialNumber = tagStore.ReadString(serialNumberTag);

                    MESIntegration mesIntegration = new MESIntegration();
                    var result = mesIntegration.Serial_GetPartTrackingAnnounceSerial(station, serialNumber);

                    // Extracting ErrorCode, ErrorDescription, and NextOperation from the result
                    int errorCode = result.ErrorCode;
                    string errorDescription = result.ErrorDescription;
                    string nextOperation = result.NextOperation ?? "N/A"; // Using ?? to handle nulls

                    // Log the result in the dialog box
                    Dispatcher.Invoke(() => Log(opName, $"Invoking method Serial_GetPartTrackingAnnounceSerial for station: {station}, SerialNumber: {serialNumber} returned ErrorCode: {errorCode}, ErrorDescription: {errorDescription}, NextOperation: {nextOperation}\n\n"));

                    // Write the results to CIMPLE.response array
                    var responseTag = tagStore.GetStringArray($"{opName}.Response", 3); // Array length is 3

                    if (string.IsNullOrEmpty(errorDescription))
                    {
                        // If ErrorDescription is empty or null, provide a default value
                        errorDescription = "No error description available";
                    }

                    if (errorDescription.Length > 80)
                    {
                        errorDescription = errorDescription.Substring(0, 80);
                    }

                    tagStore.WriteStringArray(responseTag, new string[] { errorCode.ToString(), errorDescription, nextOperation });

                    // Update CIMPLE.Status to "done"
                    var statusTag = tagStore.GetString($"{opName}.Status");
                    tagStore.WriteString(statusTag, "done");

                }

            }
            catch (Exception ex)
            {
                // this will catch *any* exception from *any* branch above
                Console.WriteLine($"[Trigger {triggerValue} / {opName}] unhandled exception: {ex}");
                Dispatcher.Invoke(() => Log(opName, $"Error executing trigger {triggerValue} on {opName}: {ex.Message}\n"));
            }

        }

       

        private void LoadSettings()
        {
            ipPart1.Text = Properties.Settings.Default.IpPart1;
            ipPart2.Text = Properties.Settings.Default.IpPart2;
            ipPart3.Text = Properties.Settings.Default.IpPart3;
            ipPart4.Text = Properties.Settings.Default.IpPart4;
            //stationInput.Text = Properties.Settings.Default.StationName;
            //userInput.Text = Properties.Settings.Default.UserName;
            //passwordInput.Password = Properties.Settings.Default.Password;
        }


        private void StartQueueWorker()
        {
            if (queueWorker != null && !queueWorker.IsCompleted)
                return;

            queueCancellation = new CancellationTokenSource();
            queueWorker = Task.Run(() => ProcessQueue(queueCancellation.Token));
        }

        private void StopQueueWorker()
        {
            try
            {
                queueCancellation?.Cancel();
                queueSignal.Release();
            }
            catch
            {
            }
        }

        private async Task ProcessQueue(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    await queueSignal.WaitAsync(cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                if (triggerQueue.TryDequeue(out var item))
                {
                    isProcessingQueue = true;

                    try
                    {
                        ExecuteActionBasedOnTrigger(item.opName, item.trigger);
                    }
                    finally
                    {
                        isProcessingQueue = false;
                    }

                    try
                    {
                        await Task.Delay(100, cancellationToken);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                }
            }
        }


    }

}
