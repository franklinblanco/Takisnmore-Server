using System;
using System.IO.Pipes;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using System.Threading;
using System.ComponentModel.Design;
using System.Data;
using MySql.Data.MySqlClient;
using Renci.SshNet.Security.Cryptography;
using System.Collections.Generic;
using Twilio;
using Twilio.Rest.Api.V2010.Account;
using Microsoft.IdentityModel.Tokens;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore.Internal;
using System.Linq;
using System.IO;
using Twilio.Rest.Trunking.V1;

namespace Server
{
    #region Connection to clients
    public class Server
    {
        ServerRequestHandler srh = new ServerRequestHandler();
        private TcpListener serverSocket = new TcpListener(IPAddress.Parse("207.154.255.138"), 5001);

        #region Singleton Pattern
        private static Server instance = new Server();
        private Server() { }

        public static Server Instance
        {
            get { return instance; }
        }
        #endregion

        #region Main method
        static void Main()
        {
            //this is because the main method is static

            Server.Instance.StartServer();

        }
        #endregion

        #region StartServer method
        private void StartServer()
        {
            //Internal data stuff:
            SqlHandler.Instance.ConnectToDB();
            CacheHandler.Instance.AppKeys = SqlHandler.Instance.GetAppKeys();
            CacheHandler.Instance.CustomerKeys = SqlHandler.Instance.GetCustomerKeys();
            CacheHandler.Instance.AllProducts = SqlHandler.Instance.GetAllProducts();
            CacheHandler.Instance.AllShops = SqlHandler.Instance.GetAllShops();
            CacheHandler.Instance.AllCategories = SqlHandler.Instance.GetAllCategories();
            CacheHandler.Instance.AllSections = SqlHandler.Instance.GetAllSections();
            CacheHandler.Instance.UpdateCategoryItems();
            CacheHandler.Instance.UpdateSectionCategories();
            //Connection with client stuff:
            Thread ServerThread;
            OpenSocket();
            //connectionSocket.ReceiveTimeout = 5000;

            while (true)
            {
                Console.WriteLine("Awaiting for Incoming Connections...");
                TcpClient connectionSocket = serverSocket.AcceptTcpClient(); //Accept all incoming connections and give each connection a socket. (THIS WAS A HARD ONE)
                Console.WriteLine("A client has connected.");
                ServerThread = new Thread(() => EstablishConnection(connectionSocket)); //Every time a connection is accepted, initiate this thread
                ServerThread.Start();
            }
        }
        #endregion

        #region OpenSocket method
        private void OpenSocket()
        {
            serverSocket.Start();
        }
        #endregion

        #region EstablishConnection method
        private void EstablishConnection(TcpClient clientSocket) //Basically: Auth Client, then recieve commands from client
        {
            ClientAuthentication(clientSocket);
            //initiate heartbeat?
        }
        #endregion

        #region Client Auth
        private void ClientAuthentication(TcpClient clientSocket)
        {
            NetworkStream clientStream = clientSocket.GetStream();
            string clientkey = ReceiveMessage(clientStream);

            if (CacheHandler.Instance.AppKeys.ContainsKey(clientkey)) //Key validation
            {
                int permLevel = CacheHandler.Instance.AppKeys[clientkey];
                Console.WriteLine("Client has been authenticated Successfully.");
                SendMessage(clientStream, "1001");//Send auth success code

                //Here goes the second level of authentication (the human auth)
                //With the permission levels, perform a switch on the customer, delivery, business, and developer
                //Then ask for the neccessary data for each.
                bool authed = false;
                switch (permLevel) //All of these are hardcoded, if you ever change the permission ints, you must come over here and change these aswell.
                {
                    case 1: //Customer
                        authed = AuthCustomer(clientStream);
                        break;
                    case 2: //Delivery
                        break;
                    case 3: //Business
                        break;
                    case 9: //Developper
                        break;
                }


                while (authed) //The loop to always recieve commands from the client
                {
                    string clientCommand = ReceiveMessage(clientStream);

                    if (ClientRequest(clientStream, clientCommand, permLevel)) //If the method returns true, then finish the connection, since the client has ordered an exit.
                    {
                        break;
                    }
                }
                Console.WriteLine("Closing client connection...");
                clientSocket.Close();
            }
            else
            {
                Console.WriteLine("Client key is invalid. Disconnecting client...");
                SendMessage(clientStream, "1003");
                clientSocket.Close();
            }
        }
        #endregion

        #region Customer Stuff
        private bool AuthCustomer(NetworkStream clientStream)
        {
            //Ask for the phone number, if already registered in this device, sign in, if not, identify yourself nigga.

            string phonenumber = ReceiveMessage(clientStream);
            SendMessage(clientStream, "1209"); //request deviceid
            string deviceid = ReceiveMessage(clientStream);

            if (CacheHandler.Instance.CustomerKeys.ContainsKey(phonenumber) && deviceid == CacheHandler.Instance.CustomerKeys[phonenumber])
            {
                //Let them thru nigga! (verify their deviceid later on!)
                SendMessage(clientStream, "1201");
                Console.WriteLine("Customer has logged in!");
                return true;
            }
            else if (deviceid != "" && phonenumber != "")  //add else if in case the customer phone number exists but this is a different device....
            {
                SendMessage(clientStream, "1210"); //Phone number/deviceid not found in database
                string endhere = ReceiveMessage(clientStream);
                if (endhere == "1302")
                {
                    return false;
                }
                //Cockblock them with a register screen to enter their phone number!
                string verificationOTP = "";
                string OTP = SendOTP(phonenumber);
                bool canloop = true;
                while (verificationOTP != OTP && canloop)
                {
                    canloop = SendMessage(clientStream, "1207"); //Message to Request OTP confirmation
                    verificationOTP = ReceiveMessage(clientStream);
                    if (verificationOTP == "Stop")
                    {
                        return false;
                    }
                }
                if (!canloop)
                {
                    return false;
                }
                Console.WriteLine("Phone Number Validation Done!");
                SendMessage(clientStream, "1204"); //Request all details from client

                //Verify the data sent is in the correct format
                string customerdata = ReceiveMessage(clientStream);
                if (customerdata.Contains("."))//Checks the data sent before splitting it
                {
                    string[] customerinfo = customerdata.Split("."); //splits the data everywhere there's a .
                    //The arguments for the user signup. Username, password, Gender, and deviceidentifier.
                    if (customerinfo.Length == 3) //it can only have 3 elements.
                    {
                        //Check all 3 elements correctly
                        if(customerinfo[0].Length > 3 && customerinfo[0].Length < 50 /*&& customerinfo[0].Any( ch => ! Char.IsLetterOrDigit( ch ))*/)
                        { //Name has to be longer than 3 characters, no special characters, and not longer than 50 chars
                            if (customerinfo[1].Length > 3 && customerinfo[1].Length < 50)
                            { //Password has to be longer than 3 characters, and not longer than 50 chars
                                if (customerinfo[2] == "M" || customerinfo[2] == "F")
                                {
                                    if (deviceid != null && deviceid.Length < 100)
                                    {
                                        SqlHandler.Instance.RegisterCustomer(phonenumber, customerinfo[0], customerinfo[1], deviceid, customerinfo[2]); //register the customer fully in the db
                                        CacheHandler.Instance.CustomerKeys = SqlHandler.Instance.GetCustomerKeys(); //update the cache
                                        Console.WriteLine("Signup completed!");
                                        SendMessage(clientStream, "1205"); //signed up
                                        return true;
                                    }
                                }
                            }
                        }
                    }
                }
                Console.WriteLine("Invalid Customer Credentials.");
                SendMessage(clientStream, "1208"); //bad credentials
                return false;
            } else
            {
                SendMessage(clientStream, "1101");
                Console.WriteLine("Client sent empty login credentials. Closing Connection...");
                return false;
            }
        }
        private string SendOTP(string phonenumber)
        {
            //Send OTP here
            //This is the code to send a text message to a phone number.
            /* THIS WORKS BUT IT COSTS SO ACTIVATE THE COMMENTED CODE IN PUBLISHING
            const string accountSid = "AC4e0079cae538c2765b595314a64005f8";
            const string authToken = "10de2dc0bf8cc2165befd6a02183d6b6";
            */
            string customerphonenumber = "+1" + phonenumber;

            Random r = new Random();
            int OTP = r.Next(100000, 999999);

            string verificationText = OTP.ToString() + " Es tu Codigo de Verificación para TAKISNMORE. Disfrúta tu mini taki gratis! Gracias por preferirnos!";
            /*
            TwilioClient.Init(accountSid, authToken);

            var textMessage = MessageResource.Create(
            body: verificationText,
            from: new Twilio.Types.PhoneNumber("+12184195292"),
            to: new Twilio.Types.PhoneNumber(customerphonenumber)
        );
            Console.WriteLine(textMessage.Sid);
            */
            Console.WriteLine("Debug feature, please remove at launch! MESSAGE: " + verificationText);
            Console.WriteLine("Waiting for customer OTP verification...");

            return OTP.ToString();
        }
        #endregion

        #region ClientRequest
        private bool ClientRequest(NetworkStream clientStream, string request, int permlevel) //Basically: Recieve commands from client and interpret them.
        {
            ServerRequestHandler srh = new ServerRequestHandler();
            srh.Start(clientStream);
            if (request == null)
            {
                Console.WriteLine("Empty request.");
                return false;
            }
            bool ExitRequest = false; //Bool to be returned back to EC
            string[] requestArgs = request.Split('-'); //Split the request
            RequestType reqtype = RequestType.Invalid; //Made invalid by default (in case it's not recognized.)
            Console.WriteLine(requestArgs[0]);
            switch (requestArgs[0]) //Get the type of request this is
            {
                case "1101": //Stop request
                    ExitRequest = true;
                    reqtype = RequestType.Stop;
                    break;
                case "1102": //Read request
                    reqtype = RequestType.Read;
                    break;
                case "1103": //Write request
                    reqtype = RequestType.Write;
                    break;
                case "1104": //Pause request
                    reqtype = RequestType.Pause;
                    break;
                case "1105": //Verify request
                    reqtype = RequestType.Verify;
                    break;
                default: //invalid Request
                    reqtype = RequestType.Invalid;
                    ExitRequest = true;
                    break;
            }
            srh.ProcessRequest(requestArgs, reqtype, permlevel);
            //Have a solution to if the client only sends one parameter here. Inform him? maybe not, just let it take it's course. Or handle it in the ReqHandler
            //Send a code that means "you are missing parameters faggot!"
            return ExitRequest;
        }
        #endregion

        #region Encoding Methods
        private string decodeMessage(byte[] message)
        {
            return Encoding.Default.GetString(message);
        }
        private byte[] encodeMessage(string message)
        {
            return Encoding.Default.GetBytes(message);
        }
        #endregion

        #region Message sending
        public bool SendMessage(NetworkStream clientStream, string message)
        {
            //This should be the correct way for sending messages to clients, as this involves a client socket approach and is simpler in general.
            try
            {
                clientStream.Write(encodeMessage(message));
                return true;
            }
            catch (SocketException e)
            {
                Console.WriteLine(e.ToString());
            }
            catch (Exception e)
            {
                Console.WriteLine(e.ToString());
            }
            return false;
        }
        #endregion

        #region Message receiveing
        public string ReceiveMessage(NetworkStream clientStream)
        {
            try
            {
                byte[] message = new byte[1024]; //Create an empty byte array that will hold the message sent by client.
                int messageLength = clientStream.Read(message, 0, message.Length); //Store the size of the byte[].
                Array.Resize(ref message, messageLength); //Resize byte[] to the size gotten earlier.
                return decodeMessage(message);
            }
            catch (SocketException e)
            {
                Console.WriteLine(e.ToString());
                return "Stop";
            }
            catch (Exception e)
            {
                Console.WriteLine(e.ToString());
                return "Stop";
            }

        }
    }
    #endregion
    #endregion

    #region Request Handling
    public class ServerRequestHandler
    {        
        NetworkStream clientStream;

        public void Start(NetworkStream stream)
        {
            clientStream = stream;
        }

        #region ProccessRequest method
        public void ProcessRequest(string[] requestArgs, RequestType requestType, int permlevel)
        {
            switch (requestType)
            {
                case RequestType.Stop:
                    SendMessage("1101");
                    Console.WriteLine("Client has ordered a stop, closing connection...");
                    break;
                case RequestType.Invalid:
                    SendMessage("1105");
                    Console.WriteLine("Invalid Client request.");
                    break;
                case RequestType.Pause:
                    SendMessage("1301");
                    Console.WriteLine("Client has requested a pause. Pausing Connection...");
                    break;
                case RequestType.Read:
                    Console.WriteLine("Client has requested a read. Reading...");
                    switch (requestArgs[1])
                    {
                        case "Categories": //Give the client 3 categories per request. Request has to be as follows: Categories-Sectionid-Page (page means the multiple of 3 that the client wants)
                            if (requestArgs.Length < 4)
                            {
                                SendMessage("1304"); //missing arguments
                                Console.WriteLine("Client is missing arguments.");
                                break;
                            }
                            if (CacheHandler.Instance.AllSections.ContainsKey(requestArgs[2]))
                            {
                                int pagenumber = Int16.Parse(requestArgs[3]);
                                string[] categoriesinsection = CacheHandler.Instance.SectionCategories[requestArgs[2]];
                                if (categoriesinsection.Length < 1) { SendMessage("1305"); break; } //Means there's no categories in this section
                                string categories = "";
                                for (int x = (pagenumber * 3) - 3; x < pagenumber * 3; x++)
                                {
                                    if (categoriesinsection.Length > x)
                                    {
                                        Category category = CacheHandler.Instance.AllCategories[categoriesinsection[x]];
                                        categories += category.id + ":" + category.title + "/";
                                    }
                                }
                                if (categories.Length < 1) { SendMessage("1305"); break; } //Means there's no categories in this section
                                categories = categories.Remove(categories.Length - 1);
                                SendMessage(categories);
                                break;
                            }
                            
                            break;
                        case "CategoryItems":
                            if (requestArgs.Length < 3)
                            {
                                SendMessage("1304"); //missing arguments
                                Console.WriteLine("Client is missing arguments.");
                                break;
                            }
                            string categoryid = requestArgs[2];
                            if (CacheHandler.Instance.AllCategories.ContainsKey(categoryid)) //Lookup for the category key in the dictionary
                            {
                                //New Format(bottom) For loop that joins all the categories (3 at the moment per category)
                                string categoryitems = "";
                                string[] productids = CacheHandler.Instance.CategoryItems[categoryid];
                                
                                for (int x = 0; x < 3; x++)
                                {
                                    if (x < productids.Length)
                                    {
                                        Product product = CacheHandler.Instance.AllProducts[productids[x]];

                                        string productmediaids = "";
                                        foreach (string mediaid in product.pictureids)
                                        {
                                            productmediaids += mediaid + ",";
                                        }
                                        productmediaids = productmediaids.Remove(productmediaids.Length - 1);

                                        categoryitems += product.itemname + ":"
                                            + product.itemprice.ToString() + ":"
                                            + product.description + ":" //2nd parameter
                                            + CacheHandler.Instance.AllShops[product.shopid].itemname + ":" 
                                            + productmediaids + ":" //4th parameter
                                            + CacheHandler.Instance.AllShops[product.shopid].logoid + ":"
                                            + product.id + "/"; //6th parameter
                                    }
                                }
                                categoryitems = categoryitems.Remove(categoryitems.Length - 1);
                                SendMessage(categoryitems);
                            }
                            break;
                        case "Sections":
                            if (CacheHandler.Instance.AllSections.Count > 0)
                            {
                                string AllSectionNames = "";
                                foreach (Section section in CacheHandler.Instance.AllSections.Values)
                                {
                                    AllSectionNames += section.title + "/";
                                }
                                AllSectionNames = AllSectionNames.Remove(AllSectionNames.Length - 1);
                                SendMessage(AllSectionNames);
                            }
                            break;
                        case "Section":
                            if (requestArgs.Length < 3)
                            {
                                SendMessage("1304"); //missing arguments
                                Console.WriteLine("Client is missing arguments.");
                                break;
                            }
                            string sectionid = requestArgs[2];
                            if (CacheHandler.Instance.SectionCategories.ContainsKey(sectionid))
                            {
                                string[] categoryids = CacheHandler.Instance.SectionCategories[sectionid];
                                string sectioncategories = "";
                                foreach (string catid in categoryids)
                                {
                                    Category category = CacheHandler.Instance.AllCategories[catid];
                                    sectioncategories += category.id + "," + category.title + ",";
                                    if(category.issearchable) { sectioncategories += "true";  }
                                    sectioncategories += "/";
                                    //Category id, Category title, issearchable true or nothing then /
                                }
                                sectioncategories = sectioncategories.Remove(sectioncategories.Length - 1);
                                SendMessage(sectioncategories);

                            }
                            break;
                        case "Media":
                            if (requestArgs.Length < 3)
                            {
                                SendMessage("1304"); //missing arguments
                                Console.WriteLine("Client is missing arguments.");
                                break;
                            }
                            string itemmedia = requestArgs[2];

                            byte[] media = CacheHandler.Instance.getmedia(itemmedia);
                            if (media != null)
                            {
                                if (SendBytes(media))
                                {
                                    Console.WriteLine("Bytes sent correctly!");
                                }
                                else
                                {
                                    Console.WriteLine("Error sending bytes.");
                                }
                            }
                            break; //Case media
                    }
                    break;
                case RequestType.Verify:
                    break;
                case RequestType.Write:
                    break;
            }
        }
        #endregion

        #region Message sending
        private bool SendMessage(string message)
        {
            //This should be the correct way fo sending messages to clients, as this involves a client socket approach and is simpler in general.
            try
            {
                Server.Instance.SendMessage(clientStream, message);
                return true;
            }
            catch (SocketException e)
            {
                Console.WriteLine(e.ToString());
            }
            catch (Exception e)
            {
                Console.WriteLine(e.ToString());
            }
            return false;
        }
        private bool SendBytes(byte[] bytes)
        {
            try
            {
                Console.WriteLine("Amount of bytes being sent: " + bytes.Length);
                clientStream.Write(AddHeader(bytes));
                clientStream.Flush();
                return true;
            }
            catch (SocketException e)
            {
                Console.WriteLine(e.ToString());
            }
            catch (Exception e)
            {
                Console.WriteLine(e.ToString());
            }
            return false;
        }
        #endregion

        #region Header Handling

        private byte[] AddHeader(byte[] packet)
        {
            int packetlength = packet.Length;
            bool is32bitint;
            byte[] packetwithheader;

            if (packetlength <= 32767 && packetlength > 0)
            {
                is32bitint = false;
                packetwithheader = new byte[packetlength + 3];
                packetwithheader = new byte[] { Convert.ToByte(is32bitint) }.Concat(BitConverter.GetBytes(packetlength)).Concat(packet).ToArray(); //Sum the boolean byte[], the int byte[] and the packet 
            }
            else if (packetlength > 32767 && packetlength <= 2147483647)
            {
                is32bitint = true;
                packetwithheader = new byte[packetlength + 5];
                packetwithheader = new byte[] { Convert.ToByte(is32bitint) }.Concat(BitConverter.GetBytes(packetlength)).Concat(packet).ToArray(); //Sum the boolean byte[], the int byte[] and the packet 
            }
            else
            {
                packetwithheader = null;
                Console.WriteLine("Fatal error, either packet length is too big, or too small");
            }
            Console.WriteLine("Packaged " + packetwithheader.Length + " bytes.");
            return packetwithheader;
        }

        private int ReadHeader(byte[] packet)
        {
            bool isint32 = BitConverter.ToBoolean(packet, 0);
            if (!isint32)
            {

                Int16 size = BitConverter.ToInt16(packet, 1);
                Console.WriteLine("It's a 16 bit int, Of length " + size); //Remove
                return size;
            }
            else
            {
                Int32 size = BitConverter.ToInt32(packet, 1);
                Console.WriteLine("It's a 32 bit int, Of length " + size); //Remove
                return size;
            }
        }

        private byte[] RemoveHeader(int packetsize, int filesize, byte[] packet)
        {
            int difference = 5;
            if (filesize <= 32767)
            {
                difference = 3;
            }
            List<byte> Packet = packet.ToList();
            Console.WriteLine("Packet size when converted to list " + Packet.Count() + " difference: " + difference); //Remove
            Packet.RemoveRange(0, difference);
            Console.WriteLine("Removed " + difference + " elements from the packet."); //Remove
            return Packet.ToArray();
        }

        #endregion


    }
    #endregion

    #region SqlHandler
    public class SqlHandler
    {

        #region Singleton Pattern
        private static SqlHandler instance = new SqlHandler();
        private SqlHandler() { }

        public static SqlHandler Instance
        {
            get { return instance; }
        }
        #endregion


        private MySqlConnection connection; //The connection reference to the MYSQL server


        #region Connection to the MYSQL Methods
        public void ConnectToDB() //Method that connects to DB
        {
            string connectionString = "Server=takisnmore-mysql-db-do-user-7996753-0.b.db.ondigitalocean.com;" +
                "Port=25060;" +
                "Database=Takisnmore_App_DB;" +
                "Uid=doadmin;" +
                "Pwd=qx9gbxazqldi1x7b;" +
                "SSL Mode=Required";
            connection = new MySqlConnection(connectionString);
            try
            {
                connection.Open();
            }
            catch (Exception e)
            {
                Console.WriteLine(e.ToString());
            }
            Console.WriteLine("Connected to the SQL Server Correctly!");
        }
        public void DisconnectFromDB()
        {
            connection.Close();
        }
        #endregion

        #region Getting Keys from MYSQL Methods
        public Dictionary<string, int> GetAppKeys() //This method stores all the appkeys into the Dictionary (should only be done at the start of the server).
        {
            Dictionary<string, int> appKeys = new Dictionary<string, int>(); //create the dictionary to enter the values & keys in
            MySqlCommand command = new MySqlCommand();
            command.Connection = connection;
            command.CommandText = "SELECT App_Key, permissionlevel FROM ClientAppKeys"; //This is the query to select the data from the appkeys table and store it
            MySqlDataReader reader; //In a Dictionary<string, int> The Key, and the permission level.
            reader = command.ExecuteReader();
            while (reader.Read()) //This code runs until it finds the end of the table
            {
                appKeys.Add(reader.GetString(0), reader.GetInt16(1));
            }
            reader.Close();
            return appKeys;

        }
        public Dictionary<string, string> GetCustomerKeys()
        {
            Dictionary<string, string> CustomerKeys = new Dictionary<string, string>(); //create the dictionary to enter the values & keys in
            MySqlCommand command = new MySqlCommand();
            command.Connection = connection;
            command.CommandText = "SELECT phonenumber, deviceidentifier FROM CustomerAccounts"; //This is the query to select the data from the appkeys table and store it
            MySqlDataReader reader; //In a Dictionary<string, int> The Key, and the permission level.
            reader = command.ExecuteReader();
            while (reader.Read()) //This code runs until it finds the end of the table
            {
                CustomerKeys.Add(reader.GetString(0), reader.GetString(1));
            }
            reader.Close();
            return CustomerKeys;
        }

        public Dictionary<string, Product> GetAllProducts()
        {
            Dictionary<string, Product> allproducts = new Dictionary<string, Product>();
            MySqlCommand command = new MySqlCommand();
            command.Connection = connection;
            command.CommandText = "SELECT * FROM Products";
            MySqlDataReader reader;
            reader = command.ExecuteReader();
            while (reader.Read())
            {
                Product product = new Product
                {
                    id = reader.GetString(0),
                    itemname = reader.GetString(1),
                    description = reader.GetString(2),
                    itemprice = reader.GetDecimal(3),
                    pictureids = reader.GetString(4).Split(","),
                    discountpercent = reader.GetDecimal(5),
                    categoriesid = reader.GetString(6).Split(","),
                    shopid = reader.GetString(7)
                };
                allproducts.Add(product.id, product);
            }
            reader.Close();
            return allproducts;
        }

        public Dictionary<string, Shop> GetAllShops()
        {
            Dictionary<string, Shop> allshops = new Dictionary<string, Shop>();
            MySqlCommand command = new MySqlCommand();
            command.Connection = connection;
            command.CommandText = "SELECT * FROM Shops";
            MySqlDataReader reader;
            reader = command.ExecuteReader();
            while (reader.Read())
            {
                Shop shop = new Shop
                {
                    id = reader.GetString(0),
                    itemname = reader.GetString(1),
                    owneraccountid = reader.GetString(2),
                    description = reader.GetString(3),
                    logoid = reader.GetString(4),
                    phonenumber = reader.GetString(5)
                };
                allshops.Add(shop.id, shop);
            }
            reader.Close();
            return allshops;
        }

        public Dictionary<string, Category> GetAllCategories()
        {
            Dictionary<string, Category> allcategories = new Dictionary<string, Category>();
            MySqlCommand command = new MySqlCommand();
            command.Connection = connection;
            command.CommandText = "SELECT * FROM Categories";
            MySqlDataReader reader;
            reader = command.ExecuteReader();
            while (reader.Read())
            {
                Category category = new Category
                {
                    id = reader.GetString(0),
                    title = reader.GetString(1),
                    issearchable = reader.GetBoolean(2),
                    sectionid = reader.GetString(3)
                };
                allcategories.Add(category.id, category);
            }
            reader.Close();
            return allcategories;
        }

        public Dictionary<string, Section> GetAllSections()
        {
            Dictionary<string, Section> allsections = new Dictionary<string, Section>();
            MySqlCommand command = new MySqlCommand();
            command.Connection = connection;
            command.CommandText = "SELECT * FROM Sections";
            MySqlDataReader reader;
            reader = command.ExecuteReader();
            while (reader.Read())
            {
                Section section = new Section
                {
                    id = reader.GetString(0),
                    title = reader.GetString(1)
                };
                allsections.Add(section.id, section);
            }
            reader.Close();
            return allsections;
        }

        #endregion

        #region Custom MYSQL Methods

        public bool RegisterCustomer(string phonenumber, string name, string password, string deviceid, string gender)
        {
            if (phonenumber.Length != 10)
            {
                Console.WriteLine("The phone number given is not 10 characters long.");
                return false; //Invalid phone number size
            }
            if (Regex.IsMatch(phonenumber, @"[a-zA-Z]+$"))
            {
                Console.WriteLine("The phone number given contains invalid characters.");
                return false; //Invalid phone number characters
            }
            if (gender != "M" && gender != "F")
            {
                Console.WriteLine("Invalid Gender");
                return false; //Invalid Gender string
            }
            return WriteDataIn("CustomerAccounts", new string[] { phonenumber, name, password, deviceid, gender, "0"});
        }

        #endregion

        #region Standard MYSQL formula Methods
        public string ReadDataWhere(string getColumn, string table, string whereColumn, string whereValue) //A test method to read a specific data from a table
        {
            MySqlCommand command = new MySqlCommand();
            command.Connection = connection;
            command.CommandText = "SELECT " + getColumn + " from " + table + " WHERE " + whereColumn + " = @whereval";
            command.Parameters.AddWithValue("@whereval", whereValue); //This is how to parametrize values in an sql Command.
            try
            {
                string value = command.ExecuteScalar().ToString();
                return value;
            }
            catch (Exception e)
            {
                Console.WriteLine(e.ToString());
            }
            return "0";
        }
        public bool WriteDataIn(string table, string[] values) //This method writes data to a specific table (for all the values)
        {
            MySqlCommand command = new MySqlCommand();
            command.Connection = connection;

            string allvalues = "";
            for (int x = 0; x < values.Length; x++)
            {
                allvalues += "'" + values[x] + "'";
                if (x != values.Length - 1)
                {
                    allvalues += ", ";
                }

            }

            Console.WriteLine("Wrote " + allvalues);

            command.CommandText = "INSERT IGNORE INTO " + table + " VALUES (" + allvalues + ")";

            try
            {
                if (command.ExecuteNonQuery() > 0)
                {
                    return true;
                }
                else
                {
                    Console.WriteLine("Number of rows affected was 0. Check the key values. Probably a duplicate Unique key.");
                    return false;
                }
            }
            catch (Exception e)
            {
                Console.WriteLine(e.ToString());
            }
            return false;
        }
        public bool CreateTable(string tablename) //Method to create a table (Use with caution)
        {
            /*This method needs a lot of work. The server must be able to create tables and their respective columns,
             Such as the item category or itemtag tables, that each have their own set of columns, maybe implement
             an Approach that just takes in all the parameters you give it, and types it one by one into a string.
             Or just make a string that has all the parameters?*/
            MySqlCommand command = new MySqlCommand();
            command.Connection = connection;

            command.CommandText = "CREATE TABLE IF NOT EXISTS " + tablename;

            try
            {
                command.ExecuteNonQuery();
                return true;
            } catch (Exception e)
            {
                Console.WriteLine(e.ToString());
            }


            return false;
        }
        #endregion
    }
    #endregion

    #region CacheHandler
    public class CacheHandler
    {
        #region Singleton Pattern
        private static CacheHandler instance = new CacheHandler();
        private CacheHandler() { }

        public static CacheHandler Instance
        {
            get { return instance; }
        }
        #endregion

        public Dictionary<string, int> AppKeys = new Dictionary<string, int>();
        public Dictionary<string, string> CustomerKeys = new Dictionary<string, string>(); //this dictionary holds the phone number as a key and the identifier of the phone.
        public Dictionary<string, Product> AllProducts = new Dictionary<string, Product>(); //This Dictionary holds all the products availible from the DB in the Cache. Update regularly.
        public Dictionary<string, Shop> AllShops = new Dictionary<string, Shop>(); //This Dictionary holds all the Shops availible from the DB in the Cache. Update regularly.
        public Dictionary<string, Category> AllCategories = new Dictionary<string, Category>();
        public Dictionary<string, string[]> CategoryItems = new Dictionary<string, string[]>();
        public Dictionary<string, string[]> SectionCategories = new Dictionary<string, string[]>();
        public Dictionary<string, Section> AllSections = new Dictionary<string, Section>();

        private const string mediapath = "/media/";

        public byte[] getmedia(string name)
        {

            string fullfilepath = mediapath + GetPath(name);

            if (!File.Exists(fullfilepath))
            {
                return null;
            }
            byte[] file = File.ReadAllBytes(fullfilepath);
            return file;
        }

        public string GetPath(string filename) //this method is to get the folder in which the file is located, inside of Media.
        {
            char[] dividedfilename = filename.ToCharArray(); //Splits the string into an array of chars to see where is the file.

            string filefolder = "";

            switch (dividedfilename[0]) //Perform checks on the first Char of the filename, to see which folder it is located.
            {
                case 'I':
                    filefolder = "images/";
                    break;
                case 'L':
                    filefolder = "logos/";
                    break;
                    //Add the other folders (located in /media/), but for now this is okay.
            }
            string format = string.Concat(dividedfilename[1], dividedfilename[2]);
            string extension = "";
            switch (format) //Perform checks on the second and third chars of the array to get the extension of the file.
            {
                case "JP":
                    extension = ".jpg";
                    break;
                case "JE":
                    extension = ".jpeg";
                    break;
                case "PN":
                    extension = ".png";
                    break;
                    //Add all the other file formats you support. 
            }
            return filefolder + filename + extension;
        }

        public void UpdateCategoryItems()
        {
            CategoryItems.Clear();
            foreach (Category category in AllCategories.Values)
            {
                string id = category.id;
                List<string> productsincategory = new List<string>();
                foreach (Product product in AllProducts.Values)
                {
                    foreach (string s in product.categoriesid)
                    {
                        if (s == id) { productsincategory.Add(product.id); }
                    }
                }
                CategoryItems.Add(id, productsincategory.ToArray());
            }
        }
        public void UpdateSectionCategories()
        {
            SectionCategories.Clear();
            foreach (Section section in AllSections.Values)
            {
                string id = section.id;
                List<string> categoriesinsection = new List<string>();
                foreach (Category category in AllCategories.Values)
                {
                    if (category.sectionid == id)
                    {
                        categoriesinsection.Add(category.id);
                    }
                }
                SectionCategories.Add(id, categoriesinsection.ToArray());
            }
        }
    }
    #endregion

    #region Enum types
    public enum RequestType
    { 
        Stop,
        Read,
        Write,
        Pause,
        Verify,
        Invalid

    }
        #endregion
}
