using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace FormPegarMAC
{
    public partial class Form1 : Form
    {
        public Form1()
        {
            InitializeComponent();

            
        }

        List<string> MACs = new List<string>(); //Lista para armazenar os Macs
        List<string> Temp = new List<string>();
       
        /// <summary>
        /// MIB_IPNETROW structure returned by GetIpNetTable
        /// DO NOT MODIFY THIS STRUCTURE.
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        struct MIB_IPNETROW
        {
            [MarshalAs(UnmanagedType.U4)]
            public int dwIndex;
            [MarshalAs(UnmanagedType.U4)]
            public int dwPhysAddrLen;
            [MarshalAs(UnmanagedType.U1)]
            public byte mac0;
            [MarshalAs(UnmanagedType.U1)]
            public byte mac1;
            [MarshalAs(UnmanagedType.U1)]
            public byte mac2;
            [MarshalAs(UnmanagedType.U1)]
            public byte mac3;
            [MarshalAs(UnmanagedType.U1)]
            public byte mac4;
            [MarshalAs(UnmanagedType.U1)]
            public byte mac5;
            [MarshalAs(UnmanagedType.U1)]
            public byte mac6;
            [MarshalAs(UnmanagedType.U1)]
            public byte mac7;
            [MarshalAs(UnmanagedType.U4)]
            public int dwAddr;
            [MarshalAs(UnmanagedType.U4)]
            public int dwType;
        }

        /// <summary>
        /// GetIpNetTable external method
        /// </summary>
        /// <param name="pIpNetTable"></param>
        /// <param name="pdwSize"></param>
        /// <param name="bOrder"></param>
        /// <returns></returns>
        [DllImport("IpHlpApi.dll")]
        [return: MarshalAs(UnmanagedType.U4)]
        static extern int GetIpNetTable(IntPtr pIpNetTable,
              [MarshalAs(UnmanagedType.U4)] ref int pdwSize, bool bOrder);

        /// <summary>
        /// Error codes GetIpNetTable returns that we recognise
        /// </summary>
        const int ERROR_INSUFFICIENT_BUFFER = 122;
        /// <summary>
        /// Get the IP and MAC addresses of all known devices on the LAN
        /// </summary>
        /// <remarks>
        /// 1) This table is not updated often - it can take some human-scale time 
        ///    to notice that a device has dropped off the network, or a new device
        ///    has connected.
        /// 2) This discards non-local devices if they are found - these are multicast
        ///    and can be discarded by IP address range.
        /// </remarks>
        /// <returns></returns>
        private static Dictionary<IPAddress, PhysicalAddress> GetAllDevicesOnLAN()
        {
            Dictionary<IPAddress, PhysicalAddress> all = new Dictionary<IPAddress, PhysicalAddress>();
            // Add this PC to the list...
            all.Add(GetIPAddress(), GetMacAddress());
            int spaceForNetTable = 0;
            // Get the space needed
            // We do that by requesting the table, but not giving any space at all.
            // The return value will tell us how much we actually need.
            GetIpNetTable(IntPtr.Zero, ref spaceForNetTable, false);
            // Allocate the space
            // We use a try-finally block to ensure release.
            IntPtr rawTable = IntPtr.Zero;
            try
            {
                rawTable = Marshal.AllocCoTaskMem(spaceForNetTable);
                // Get the actual data
                int errorCode = GetIpNetTable(rawTable, ref spaceForNetTable, false);
                if (errorCode != 0)
                {
                    // Failed for some reason - can do no more here.
                    throw new Exception(string.Format(
                      "Unable to retrieve network table. Error code {0}", errorCode));
                }
                // Get the rows count
                int rowsCount = Marshal.ReadInt32(rawTable);
                IntPtr currentBuffer = new IntPtr(rawTable.ToInt64() + Marshal.SizeOf(typeof(Int32)));
                // Convert the raw table to individual entries
                MIB_IPNETROW[] rows = new MIB_IPNETROW[rowsCount];
                for (int index = 0; index < rowsCount; index++)
                {
                    rows[index] = (MIB_IPNETROW)Marshal.PtrToStructure(new IntPtr(currentBuffer.ToInt64() +
                                                (index * Marshal.SizeOf(typeof(MIB_IPNETROW)))
                                               ),
                                                typeof(MIB_IPNETROW));
                }
                // Define the dummy entries list (we can discard these)
                PhysicalAddress virtualMAC = new PhysicalAddress(new byte[] { 0, 0, 0, 0, 0, 0 });
                PhysicalAddress broadcastMAC = new PhysicalAddress(new byte[] { 255, 255, 255, 255, 255, 255 });
                foreach (MIB_IPNETROW row in rows)
                {
                    IPAddress ip = new IPAddress(BitConverter.GetBytes(row.dwAddr));
                    byte[] rawMAC = new byte[] { row.mac0, row.mac1, row.mac2, row.mac3, row.mac4, row.mac5 };
                    PhysicalAddress pa = new PhysicalAddress(rawMAC);
                    if (!pa.Equals(virtualMAC) && !pa.Equals(broadcastMAC) && !IsMulticast(ip))
                    {
                        //Console.WriteLine("IP: {0}\t\tMAC: {1}", ip.ToString(), pa.ToString());
                        if (!all.ContainsKey(ip))
                        {
                            all.Add(ip, pa);
                        }
                    }
                }
            }
            finally
            {
                // Release the memory.
                Marshal.FreeCoTaskMem(rawTable);
            }
            return all;
        }

        /// <summary>
        /// Gets the IP address of the current PC
        /// </summary>
        /// <returns></returns>
        private static IPAddress GetIPAddress()
        {
            String strHostName = Dns.GetHostName();
            IPHostEntry ipEntry = Dns.GetHostEntry(strHostName);
            IPAddress[] addr = ipEntry.AddressList;
            foreach (IPAddress ip in addr)
            {
                if (!ip.IsIPv6LinkLocal)
                {
                    return (ip);
                }
            }
            return addr.Length > 0 ? addr[0] : null;
        }

        /// <summary>
        /// Gets the MAC address of the current PC.
        /// </summary>
        /// <returns></returns>
        private static PhysicalAddress GetMacAddress()
        {
            foreach (NetworkInterface nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                // Only consider Ethernet network interfaces
                if (nic.NetworkInterfaceType == NetworkInterfaceType.Ethernet &&
                    nic.OperationalStatus == OperationalStatus.Up)
                {
                    return nic.GetPhysicalAddress();
                }
            }
            return null;
        }

        /// <summary>
        /// Returns true if the specified IP address is a multicast address
        /// </summary>
        /// <param name="ip"></param>
        /// <returns></returns>
        private static bool IsMulticast(IPAddress ip)
        {
            bool result = true;
            if (!ip.IsIPv6Multicast)
            {
                byte highIP = ip.GetAddressBytes()[0];
                if (highIP < 224 || highIP > 239)
                {
                    result = false;
                }
            }
            return result;
        }

        private void Form1_Load(object sender, EventArgs e)
        {
            // Get my PC IP address
            richTextBox2.Text += "IP :     " +  GetIPAddress() + "\n";
            // Get My PC MAC address
            richTextBox2.Text += "MAC : " + GetMacAddress() + "\n";
            // Get all devices on network
            Dictionary<IPAddress, PhysicalAddress> all = GetAllDevicesOnLAN();
            foreach (KeyValuePair<IPAddress, PhysicalAddress> kvp in all)
            {
                richTextBox1.Text += "IP :     " + kvp.Key + "\n" + "MAC : " + kvp.Value + "\n";
                richTextBox1.Text += "-------------------------------------------\n";
                MACs.Add(kvp.Value.ToString());
            }
        }

        private void button1_Click(object sender, EventArgs e)
        {
            string docPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

            // Write the string array to a new file named "ListaMACs.txt".
            using (StreamWriter outputFile = new StreamWriter(Path.Combine(docPath, "ListaMACs.txt")))
            {
                foreach (string mac in MACs)
                    outputFile.WriteLine(mac);
            }
            MessageBox.Show("Documento ListaMACs.txt salvo na pasta Documentos");
            richTextBox3.Text = "";
            richTextBox4.Text = "";

            try
            {
                //Ler txt
                
                string[] lines = File.ReadAllLines(Path.Combine(docPath, "ListaMACs.txt"));

                foreach (string line in lines)
                {

                    richTextBox4.Text += "MAC : " + line + " \n";
                    Temp.Add(line);

                }

                var intersect = MACs.Where(a => !Temp.Select(b => b.ToString()).Contains(a.ToString()));
                foreach (var inter in intersect)
                {
                    richTextBox3.Text += inter + "\n";
                }



            }
            catch (Exception)
            {

                MessageBox.Show("Arquivo não localizado");
                return;
            }
        }

        private void button2_Click(object sender, EventArgs e)
        {
            richTextBox3.Text = "";
            richTextBox4.Text = "";

            try
            {
                //Ler txt
                string docPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                string[] lines = File.ReadAllLines(Path.Combine(docPath, "ListaMACs.txt"));

                foreach (string line in lines)
                {

                    richTextBox4.Text += "MAC : " + line + " \n";
                    Temp.Add(line);

                }

                var intersect = MACs.Where(a => !Temp.Select(b => b.ToString()).Contains(a.ToString()));
                foreach (var inter in intersect)
                {
                    richTextBox3.Text += inter + "\n";
                }

                MessageBox.Show("Documento ListaMACs.txt carregado com sucesso");                 
              
            }
            catch (Exception)
            {
                    
                MessageBox.Show("Arquivo não localizado");
                return;
            }
            
            
        }

        private void button3_Click(object sender, EventArgs e)
        {
            richTextBox1.Text = "";
            richTextBox2.Text = "";
            richTextBox3.Text = "";
            richTextBox4.Text = "";
            // Get my PC IP address
            richTextBox2.Text += "IP :     " + GetIPAddress() + "\n";
            // Get My PC MAC address
            richTextBox2.Text += "MAC : " + GetMacAddress() + "\n";
            // Get all devices on network
            Dictionary<IPAddress, PhysicalAddress> all = GetAllDevicesOnLAN();
            foreach (KeyValuePair<IPAddress, PhysicalAddress> kvp in all)
            {
                richTextBox1.Text += "IP :     " + kvp.Key + "\n" + "MAC : " + kvp.Value + "\n";
                richTextBox1.Text += "-------------------------------------------\n";
                MACs.Add(kvp.Value.ToString());
            }

            try
            {
                //Ler txt
                string docPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                string[] lines = File.ReadAllLines(Path.Combine(docPath, "ListaMACs.txt"));

                foreach (string line in lines)
                {

                    richTextBox4.Text += "MAC : " + line + " \n";
                    Temp.Add(line);

                }

                var intersect = MACs.Where(a => !Temp.Select(b => b.ToString()).Contains(a.ToString()));
                foreach (var inter in intersect)
                {
                    richTextBox3.Text += inter + "\n";
                }



            }
            catch (Exception)
            {

                MessageBox.Show("Arquivo não localizado");
                return;
            }

        }
    }
}
