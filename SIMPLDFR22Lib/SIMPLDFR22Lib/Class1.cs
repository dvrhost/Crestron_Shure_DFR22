using System;
using System.Text;
using Crestron.SimplSharp;
using Crestron.SimplSharp.CrestronSockets;
using System.Collections.Generic;

namespace ShureDFR22E
{
    public class DFR22Communication
    {
        #region Properties
        private uint TCPBufferSize { get; set; }
        private TCPClient TcpClient { get; set; }

        private bool connected { get; set; }
        private bool initialized { get; set; }
        private string IpAddress { get; set; }

        private bool debug { get; set; }
        private CTimer reconnectTimer;
        private int Port { get; set; }
        private string DevID { get; set; }
        private bool manualDisconnect { get; set; }

        private byte[] temp = new byte[64]; //temp string from DFR unit

        private byte[] rem = new byte[64]; //remainder


        //text proto 
        private string SendMessage;

        //Status of volume level & mute states 
        private double VolInCh1 { get; set; }
        private double VolInCh2 { get; set; }
        private double VolOutCh1 { get; set; }
        private double VolOutCh2 { get; set; }

        private double VolMixPoint11 { get; set; }
        private double VolMixPoint12 { get; set; }
        private double VolMixPoint21 { get; set; }
        private double VolMixPoint22 { get; set; }
        private double FaderPreMixCh1 { get; set; }
        private double FaderPreMixCh2 { get; set; }

        private bool MuteInCh1 { get; set; }
        private bool MuteInCh2 { get; set; }
        private bool MuteOutCh1 { get; set; }
        private bool MuteOutCh2 { get; set; }

        private bool MixPointCh1toOut1 { get; set; }
        private bool MixPointCh2toOut1 { get; set; }
        private bool MixPointCh1toOut2 { get; set; }
        private bool MixPointCh2toOut2 { get; set; }

        private enum ErrorLevel { Notice, Warning, Error, None }

        #region SocketStatus Dictionary

        private Dictionary<SocketStatus, ushort> sockStatusDict = new Dictionary<SocketStatus, ushort>()
        {
            {SocketStatus.SOCKET_STATUS_NO_CONNECT, 0},
            {SocketStatus.SOCKET_STATUS_WAITING, 1},
            {SocketStatus.SOCKET_STATUS_CONNECTED, 2},
            {SocketStatus.SOCKET_STATUS_CONNECT_FAILED, 3},
            {SocketStatus.SOCKET_STATUS_BROKEN_REMOTELY, 4},
            {SocketStatus.SOCKET_STATUS_BROKEN_LOCALLY, 5},
            {SocketStatus.SOCKET_STATUS_DNS_LOOKUP, 6},
            {SocketStatus.SOCKET_STATUS_DNS_FAILED, 7},
            {SocketStatus.SOCKET_STATUS_DNS_RESOLVED, 8},
            {SocketStatus.SOCKET_STATUS_LINK_LOST,9},
            {SocketStatus.SOCKET_STATUS_SOCKET_NOT_EXIST,10}
        };

        #endregion

        #endregion

        #region SIMPL+ Delegates


        public delegate void ConnectionStatusHandler(SimplSharpString serialStatus, ushort analogStatus);
        public ConnectionStatusHandler ConnectionStatus { get; set; }

        public delegate void InitializedStatusHandler(ushort status);
        public InitializedStatusHandler InitializedStatus { get; set; }

        public delegate void DFRVolumeHandler(ushort VolIn1, ushort VolIn2, ushort VolOut1, ushort VolOut2, ushort VolMix_1_1, ushort VolMix_1_2, ushort VolMix_2_1, ushort VolMix_2_2);
        public DFRVolumeHandler DFRVolume { get; set; }

        public delegate void DFRVolumeHandlerInDb(SimplSharpString VolIn1, SimplSharpString VolIn2, SimplSharpString VolOut1, SimplSharpString VolOut2, SimplSharpString VolMix_1_1, SimplSharpString VolMix_1_2, SimplSharpString VolMix_2_1, SimplSharpString VolMix_2_2);
        public DFRVolumeHandlerInDb DFRVolumeString { get; set; }

        public delegate void DFRMuteHandler(ushort MuteIn1, ushort MuteIn2, ushort MuteOut1, ushort MuteOut2);
        public DFRMuteHandler DFRMute { get; set; }

        public delegate void DFRFaderVolumeHandler(ushort VolMix1, ushort VolMix2, SimplSharpString VolMix1String, SimplSharpString VolMix2String);
        public DFRFaderVolumeHandler DFRMixVolume { get; set; }

        public delegate void DFRMixPointHandler(ushort MixIn1ToOut1, ushort MixIn1ToOut2, ushort MixIn2ToOut1, ushort MixIn2ToOut2);
        public DFRMixPointHandler DFRMixPoint { get; set; }

        #endregion

        #region Debug Function

        private void Debug(string msg, ErrorLevel errLevel)
        {
            if (debug)
            {
                CrestronConsole.PrintLine(msg);


                if (errLevel != ErrorLevel.None)
                {
                    switch (errLevel)
                    {
                        case ErrorLevel.Notice:
                            ErrorLog.Notice(msg);
                            break;
                        case ErrorLevel.Warning:
                            ErrorLog.Warn(msg);
                            break;
                        case ErrorLevel.Error:
                            ErrorLog.Error(msg);
                            break;
                    }
                }
            }
        }

        /// <summary>
        /// enable logging to ErrorLog
        /// </summary>
        public void EnableDebug()
        {
            debug = true;
            CrestronConsole.PrintLine("Debug Enabled");
        }

        /// <summary>
        /// disable logging to ErrorLog
        /// </summary>
        public void DisableDebug()
        {
            debug = false;
            CrestronConsole.PrintLine("Debug Disabled");
        }
        #endregion

        #region TCP/IP functions

        public void Initialize(string ip, ushort port, ushort bufferSize, ushort DFRID)
        {
            if (!initialized)
            {
                if (bufferSize > 0)
                    TCPBufferSize = bufferSize;
                else
                    TCPBufferSize = 1024;

                if (DFRID.ToString().Length < 2)
                    DevID = "00" + DFRID.ToString();
                else if (DFRID.ToString().Length < 3)
                    DevID = "0" + DFRID.ToString();

                if (ip.Length > 0 && port > 0)
                {
                    TcpClient = new TCPClient(ip, (int)port, (int)TCPBufferSize);
                    TcpClient.SocketStatusChange += new TCPClientSocketStatusChangeEventHandler(ClientSocketStatusChange);
                }

                if (TcpClient.PortNumber > 0 && TcpClient.AddressClientConnectedTo != string.Empty)
                {

                    initialized = true;
                    if (InitializedStatus != null) //Notify SIMPL+ Module
                        InitializedStatus(Convert.ToUInt16(initialized));
                    Debug(string.Format("TCPClient initialized: IP: {0}, Port: {1}",
                                TcpClient.AddressClientConnectedTo, TcpClient.PortNumber), ErrorLevel.Notice);

                }
                else
                {
                    initialized = false;
                    Debug("TCPClient can't initialized, missing data", ErrorLevel.Notice);
                }
            }
        }
        public void Connect()
        {
            SocketErrorCodes err = new SocketErrorCodes();
            if (connected == false && initialized == true)
            {
                try
                {
                    manualDisconnect = false;
                    err = TcpClient.ConnectToServerAsync(ClientConnectCallBackFunction);
                    TcpClient.ReceiveDataAsync(SerialRecieveCallBack);
                    Debug(string.Format("Connection attempt: {0}, with status: {1}", TcpClient.AddressClientConnectedTo, err.ToString()), ErrorLevel.Notice);

                }
                catch (Exception e)
                {
                    Debug(string.Format("Exeption on connect with error: {0}", e.Message), ErrorLevel.Error);
                }
            }
            else
            {
                Debug("Exeption on connect: Connecting befor TCPClient initialized.", ErrorLevel.Notice);
            }
        }
        public void Disconnect()
        {
            SocketErrorCodes err = new SocketErrorCodes();

            try
            {

                manualDisconnect = true;
                connected = false;
                initialized = false;
                if (InitializedStatus != null) //Notify SIMPL+ Module
                    InitializedStatus(Convert.ToUInt16(initialized));
                err = TcpClient.DisconnectFromServer();
                Debug(string.Format("Disconnect attempt: {0}, with error: {1}", TcpClient.AddressClientConnectedTo, err.ToString()), ErrorLevel.Notice);
            }
            catch (Exception e)
            {
                Debug(string.Format("Exeption on connect with error: {0}", e.Message), ErrorLevel.Error);
            }


        }


        private void SendData(byte[] Message)
        {
            if (Message.Length > 0 && connected)
            {
                SocketErrorCodes err = new SocketErrorCodes();
                err = TcpClient.SendData(Message, Message.Length);
                Debug(string.Format("Byte data transmitted: {0}, with code: {1}", Encoding.ASCII.GetString(Message, 0, Message.Length), err), ErrorLevel.None);

            }
        }

        private void TryReconnect()
        {
            if (!manualDisconnect)
            {
                Debug("Attempting to reconnect...", ErrorLevel.None);
                reconnectTimer = new CTimer(o => { TcpClient.ConnectToServerAsync(ClientConnectCallBackFunction); }, 10000);
            }
        }

        //Events
        private void ClientSocketStatusChange(TCPClient mytcpclient, SocketStatus clientsocketstatus)
        {
            // Check to see if it just connected or disconnected
            if (ConnectionStatus != null) //Notify  if subscribe
            {
                if (sockStatusDict.ContainsKey(clientsocketstatus))
                    ConnectionStatus(clientsocketstatus.ToString(), sockStatusDict[clientsocketstatus]);
            }
            if (clientsocketstatus == SocketStatus.SOCKET_STATUS_CONNECTED)
            {
                TcpClient.ReceiveDataAsync(SerialRecieveCallBack);
            }
            else
            {
                TryReconnect();
            }
        }

        private void SerialRecieveCallBack(TCPClient myTcpClient, int numberOfBytesReceived)
        {

            byte[] rxBuffer;

            if (numberOfBytesReceived > 0)
            {
                rxBuffer = myTcpClient.IncomingDataBuffer;

                Debug(string.Format("TCP RAW Data: {0}", Encoding.ASCII.GetString(rxBuffer, 0, rxBuffer.Length)), ErrorLevel.None);
                TCPResponce(rxBuffer);
            }
            TcpClient.ReceiveDataAsync(SerialRecieveCallBack);
        }

        private void ClientConnectCallBackFunction(TCPClient TcpClient)
        {
            if (TcpClient.ClientStatus == SocketStatus.SOCKET_STATUS_CONNECTED)
                connected = true;
            else
            {
                connected = false;
                TryReconnect();
            }
        }

        #endregion

        #region Generate Events to SIMPL+

        private void DFR22Unit_MixPointChanged()
        {

            ushort _mix11, _mix12, _mix21, _mix22;
            if (DFRMixPoint != null)
            {
                if (MixPointCh1toOut1 == true)
                    _mix11 = 1;
                else
                    _mix11 = 0;
                if (MixPointCh1toOut2 == true)
                    _mix12 = 1;
                else
                    _mix12 = 0;
                if (MixPointCh2toOut1 == true)
                    _mix21 = 1;
                else
                    _mix21 = 0;
                if (MixPointCh2toOut2 == true)
                    _mix22 = 1;
                else
                    _mix22 = 0;

                DFRMixPoint(_mix11, _mix12, _mix21, _mix22);
            }
        }

        private void DFR22Unit_FaderChanged()
        {
            ushort _volmix1, _volmix2;

            if (DFRMixVolume != null)
            {
                _volmix1 = Convert.ToUInt16((FaderPreMixCh1 + 107.5) * 65535 / 117.5);
                _volmix2 = Convert.ToUInt16((FaderPreMixCh2 + 107.5) * 65535 / 117.5);

                DFRMixVolume(_volmix1, _volmix2, FaderPreMixCh1.ToString(), FaderPreMixCh2.ToString());
            }
        }

        private void DFR22Unit_MuteChanged()
        {
            ushort _in1, _in2, _out1, _out2;
            if (DFRMute != null)
            {
                if (MuteInCh1 == true)
                    _in1 = 1;
                else
                    _in1 = 0;
                if (MuteInCh2 == true)
                    _in2 = 1;
                else
                    _in2 = 0;
                if (MuteOutCh1 == true)
                    _out1 = 1;
                else
                    _out1 = 0;
                if (MuteOutCh2 == true)
                    _out2 = 1;
                else
                    _out2 = 0;

                DFRMute(_in1, _in2, _out1, _out2);
            }
        }

        private void DFR22Unit_VolumeChanged()
        {
            ushort _volin1, _volin2, _volout1, _volout2, _volmix11, _volmix12, _volmix21, _volmix22;
            if (DFRVolume != null)
            {
                _volin1 = Convert.ToUInt16((VolInCh1 + 107.5) * 65535 / 117.5);
                _volin2 = Convert.ToUInt16((VolInCh2 + 107.5) * 65535 / 117.5);
                _volout1 = Convert.ToUInt16((VolOutCh1 + 107.5) * 65535 / 117.5);
                _volout2 = Convert.ToUInt16((VolOutCh2 + 107.5) * 65535 / 117.5);
                _volmix11 = Convert.ToUInt16((VolMixPoint11 + 107.5) * 65535 / 117.5);
                _volmix12 = Convert.ToUInt16((VolMixPoint12 + 107.5) * 65535 / 117.5);
                _volmix21 = Convert.ToUInt16((VolMixPoint21 + 107.5) * 65535 / 117.5);
                _volmix22 = Convert.ToUInt16((VolMixPoint22 + 107.5) * 65535 / 117.5);

                DFRVolume(_volin1, _volin2, _volout1, _volout2, _volmix11, _volmix12, _volmix21, _volmix22);

            }
            if (DFRVolumeString != null)
            {

                DFRVolumeString(VolInCh1.ToString(), VolInCh2.ToString(), VolOutCh1.ToString(), VolOutCh2.ToString(), VolMixPoint11.ToString(), VolMixPoint12.ToString(), VolMixPoint21.ToString(), VolMixPoint22.ToString());
            }
        }

        #endregion



        private byte[] CreateArrays(int Arraylength)
        {
            byte[] message = new byte[Arraylength];
            for (int i = 0; i < message.Length; i++)
                message[i] = 0x00;
            return message;
        }
        private byte[] trimByte(byte[] input)
        {
            if (input.Length > 1)
            {
                int byteCounter = input.Length - 1;
                while (input[byteCounter] == 0x00 && byteCounter > 0)
                {
                    byteCounter--;
                }
                byte[] rv = new byte[(byteCounter + 1)];
                for (int byteCounter1 = 0; byteCounter1 < (byteCounter + 1); byteCounter1++)
                {
                    rv[byteCounter1] = input[byteCounter1];
                }
                return rv;
            }
            return null;
        }

        #region SIMPL+ Function

        public void RawDataToParseFromDFR22(string data)
        {
            if (data.Length > 0)
                ParseFromDFR(data);
        }
        public void RawDataToDFR22(string data)
        {
            if (data.Length > 0)
                SendData(Encoding.ASCII.GetBytes(data));
        }
        public void DFRVolChangeTo(ushort Vol, ushort Chanel)
        {
            double _volume;
            byte[] _message;
            _volume = (Vol * 117.5 / 65535 - 107.5);
            _message = SetVolumeAt(Convert.ToInt16(Chanel), _volume);

            if (connected)
                SendData(_message);
        }

        public void DFRMixVolumeChangeTo(ushort Vol, ushort In, ushort Out)
        {
            double _volume;
            byte[] _message;
            _volume = (Vol * 117.5 / 65535 - 107.5);
            _message = SetMixPointVolumeAt(Convert.ToInt16(In), Convert.ToInt16(Out), _volume);

            if (connected)
                SendData(_message);
        }

        public void DFRMuteChanel(ushort Chanel, ushort MuteStatus)
        {
            byte[] _message;
            bool _state;
            if (MuteStatus == 1)
                _state = true;
            else
                _state = false;
            _message = InOutMute(Convert.ToInt16(Chanel), _state);

            if (connected)
                SendData(_message);


        }

        public void DFRVolumeUP(ushort Chanel)
        {
            byte[] _message;
            _message = VolumeUP(Convert.ToInt16(Chanel));

            if (connected)
                SendData(_message);


        }

        public void DFRVolumeDown(ushort Chanel)
        {
            byte[] _message;
            _message = VolumeDown(Convert.ToInt16(Chanel));

            if (connected)
                SendData(_message);

        }

        public void DFRMuteToggle(ushort Chanel)
        {
            byte[] _message;
            _message = InOutMuteToggle(Convert.ToInt16(Chanel));

            if (connected)
                SendData(_message);

        }

        public void DFRQueryStatus()
        {
            byte[] _message;
            _message = QueryStatus();

            if (connected)
                SendData(_message);

        }

        public void DFRMixPointVolumeUp(ushort In, ushort Out)
        {
            byte[] _message;
            _message = MixPointVolUp(Convert.ToInt16(In), Convert.ToInt16(Out));

            if (connected)
                SendData(_message);

        }

        public void DFRMixPointVolumeDown(ushort In, ushort Out)
        {
            byte[] _message;
            _message = MixPointVolDown(Convert.ToInt16(In), Convert.ToInt16(Out));

            if (connected)
                SendData(_message);

        }

        public void DFRSetMixPoint(ushort In, ushort Out, ushort EnablePoint)
        {
            byte[] _message;
            bool _mixpointstate;
            if (EnablePoint == 1)
                _mixpointstate = true;
            else
                _mixpointstate = false;

            _message = SetMixPoint(Convert.ToInt16(In), Convert.ToInt16(Out), _mixpointstate);

            if (connected)
                SendData(_message);

        }

        public void DFRMixVolumeUp(ushort Chanel)
        {
            byte[] _message;
            _message = FaderUp(Convert.ToInt16(Chanel));

            if (connected)
                SendData(_message);

        }

        public void DFRMixVolumeDown(ushort Chanel)
        {
            byte[] _message;
            _message = FaderDown(Convert.ToInt16(Chanel));

            if (connected)
                SendData(_message);

        }


        #endregion

        private void TCPResponce(byte[] _Data)
        {
            bool isremainder;
            int bytecounter = 0;
            //remainder if broken message is received
            byte[] message;// message to DFR22 text protocol parcer
            byte[] working; //working string

            int count;
            StringBuilder response = new StringBuilder();

            temp = trimByte(rem);


            if (temp.Length > 0)
            {
                working = CreateArrays(temp.Length + _Data.Length);
                temp.CopyTo(working, 0);
                _Data.CopyTo(working, temp.Length);
            }
            else
            {
                working = CreateArrays(_Data.Length);
                _Data.CopyTo(working, 0);
            }

            while (bytecounter < _Data.Length)
            
            {
                if (_Data[bytecounter] == 0xD0)
                {
                    count = 0;
                    isremainder = true;
                    message = CreateArrays(32);
                    for (int bytecounter2 = bytecounter; bytecounter2 < _Data.Length; bytecounter2++)
                    {

                        if (_Data[bytecounter2] == 0xD1)
                        {
                            isremainder = false;
                            response.Append(Encoding.ASCII.GetString(message, 0, message.Length));
                            ParseFromDFR(response.ToString());
                            bytecounter = bytecounter2;
                            break;
                        }

                        else
                        {

                            if (_Data[bytecounter2] != 0xD0 || _Data[bytecounter2] != 0xD1)
                            {
                                message[count] = _Data[bytecounter2];
                                count++;

                            }

                        }
                        if (count >= message.Length)
                        {

                            bytecounter = bytecounter2;
                            break; //received broken message, drop
                        }

                    }
                    //buffer empty but end of message not found
                    if (isremainder == true)
                    {
                        rem = trimByte(message);
                        if (rem.Length > 0)
                            Debug(string.Format("Got truncated data from device: {0}", Encoding.ASCII.GetString(rem, 0, rem.Length)), ErrorLevel.None);
                    }
                }
                else

                    bytecounter++;


            }

        }


        private void ParseFromDFR(string responce)
        {
            CrestronConsole.PrintLine(responce);
            int tempVol;

            //Parse responce
            if (responce.Contains("L00"))
            {

                //Level responce
                if (responce.Contains("INP001L"))//Level Input 1
                {

                    if (responce.Length >= responce.IndexOf("L00") + 4)
                    {
                        tempVol = Convert.ToChar(responce.Substring(responce.IndexOf("L00") + 3, 1));

                        VolInCh1 = DFRToVol(tempVol);
                        Debug(string.Format("Vol In 1 from DFR is: {0}", VolInCh1.ToString()), ErrorLevel.None);
                        DFR22Unit_VolumeChanged();
                    }
                }
                else if (responce.Contains("INP002L"))
                {
                    if (responce.Length >= responce.IndexOf("L00") + 4)
                    {
                        tempVol = Convert.ToChar(responce.Substring(responce.IndexOf("L00") + 3, 1));

                        VolInCh2 = DFRToVol(tempVol);
                        Debug(string.Format("Vol In 2 from DFR is: {0}", VolInCh2.ToString()), ErrorLevel.None);
                        DFR22Unit_VolumeChanged();
                    }
                }
                else if (responce.Contains("OUT001L"))
                {
                    if (responce.Length >= responce.IndexOf("L00") + 4)
                    {
                        tempVol = Convert.ToChar(responce.Substring(responce.IndexOf("L00") + 3, 1));

                        VolOutCh1 = DFRToVol(tempVol);
                        Debug(string.Format("Vol Out 1 from DFR is: {0}", VolOutCh1.ToString()), ErrorLevel.None);
                        DFR22Unit_VolumeChanged();
                    }
                }
                else if (responce.Contains("OUT002L"))
                {
                    if (responce.Length >= responce.IndexOf("L00") + 4)
                    {
                        tempVol = Convert.ToChar(responce.Substring(responce.IndexOf("L00") + 3, 1));

                        VolOutCh2 = DFRToVol(tempVol);
                        Debug(string.Format("Vol Out 2 from DFR is: {0}", VolOutCh2.ToString()), ErrorLevel.None);
                        DFR22Unit_VolumeChanged();
                    }
                }

                else if (responce.Contains("MIX001001L"))
                {
                    if (responce.Length >= responce.IndexOf("L00") + 4)
                    {
                        tempVol = Convert.ToChar(responce.Substring(responce.IndexOf("L00") + 3, 1));

                        VolMixPoint11 = DFRToVol(tempVol);
                        Debug(string.Format("Vol Mix 1-1 from DFR is: {0}", VolMixPoint11.ToString()), ErrorLevel.None);
                        DFR22Unit_VolumeChanged();

                    }
                }
                else if (responce.Contains("MIX001002L"))
                {
                    if (responce.Length >= responce.IndexOf("L00") + 4)
                    {
                        tempVol = Convert.ToChar(responce.Substring(responce.IndexOf("L00") + 3, 1));

                        VolMixPoint12 = DFRToVol(tempVol);
                        Debug(string.Format("Vol Mix 1-2 from DFR is: {0}", VolMixPoint12.ToString()), ErrorLevel.None);
                        DFR22Unit_VolumeChanged();
                    }
                }
                else if (responce.Contains("MIX002001L"))
                {
                    if (responce.Length >= responce.IndexOf("L00") + 4)
                    {
                        tempVol = Convert.ToChar(responce.Substring(responce.IndexOf("L00") + 3, 1));

                        VolMixPoint21 = DFRToVol(tempVol);
                        Debug(string.Format("Vol Mix 2-1 from DFR is: {0}", VolMixPoint21.ToString()), ErrorLevel.None);
                        DFR22Unit_VolumeChanged();
                    }
                }
                else if (responce.Contains("MIX002002L"))
                {
                    if (responce.Length >= responce.IndexOf("L00") + 4)
                    {
                        tempVol = Convert.ToChar(responce.Substring(responce.IndexOf("L00") + 3, 1));

                        VolMixPoint22 = DFRToVol(tempVol);
                        Debug(string.Format("Vol Mix 2-2 from DFR is: {0}", VolMixPoint22.ToString()), ErrorLevel.None);
                        DFR22Unit_VolumeChanged();
                    }
                }
                else if (responce.Contains("MIX001OUTL"))
                {
                    if (responce.Length >= responce.IndexOf("L00") + 4)
                    {
                        tempVol = Convert.ToChar(responce.Substring(responce.IndexOf("L00") + 3, 1));

                        FaderPreMixCh1 = DFRToVol(tempVol);
                        Debug(string.Format("Vol PreMix Ch 1 from DFR is: {0}", FaderPreMixCh1.ToString()), ErrorLevel.None);
                        DFR22Unit_FaderChanged();
                    }
                }
                else if (responce.Contains("MIX002OUTL"))
                {
                    if (responce.Length >= responce.IndexOf("L00") + 4)
                    {
                        tempVol = Convert.ToChar(responce.Substring(responce.IndexOf("L00") + 3, 1));

                        FaderPreMixCh2 = DFRToVol(tempVol);
                        Debug(string.Format("Vol PreMix Ch 2 from DFR is: {0}", FaderPreMixCh2.ToString()), ErrorLevel.None);
                        DFR22Unit_FaderChanged();
                    }
                }
            }
            else if (responce.Contains("M00"))
            {


                //Mute responce
                if (responce.Contains("INP001M001"))
                {
                    MuteInCh1 = true;
                    DFR22Unit_MuteChanged();
                }
                else if (responce.Contains("INP001M000"))
                {
                    MuteInCh1 = false;
                    DFR22Unit_MuteChanged();
                }
                else if (responce.Contains("INP002M001"))
                {
                    MuteInCh2 = true;
                    DFR22Unit_MuteChanged();
                }
                else if (responce.Contains("INP002M000"))
                {
                    MuteInCh2 = false;
                    DFR22Unit_MuteChanged();
                }
                else if (responce.Contains("OUT001M001"))
                {
                    MuteOutCh1 = true;
                    DFR22Unit_MuteChanged();
                }
                else if (responce.Contains("OUT001M000"))
                {
                    MuteOutCh1 = false;
                    DFR22Unit_MuteChanged();
                }
                else if (responce.Contains("OUT002M001"))
                {
                    MuteOutCh2 = true;
                    DFR22Unit_MuteChanged();
                }
                else if (responce.Contains("OUT002M000"))
                {
                    MuteOutCh2 = false;
                    DFR22Unit_MuteChanged();
                }
                Debug(string.Format("Get Mute change responce: Mute In 1 {0}, Mute In 2 {1}, Mute Out 1 {2}, Mute Out 2 {3}", MuteInCh1.ToString(), MuteInCh2.ToString(), MuteOutCh1.ToString(), MuteOutCh2.ToString()), ErrorLevel.None);
            }
            else if (responce.Contains("C00") && responce.Length > 18)
            {
                //MixPointResponce
                if (responce.Contains("MIX001001C001"))
                {
                    MixPointCh1toOut1 = true;
                    DFR22Unit_MixPointChanged();
                }
                else if (responce.Contains("MIX001001C000"))
                {
                    MixPointCh1toOut1 = false;
                    DFR22Unit_MixPointChanged();
                }
                else if (responce.Contains("MIX001002C001"))
                {
                    MixPointCh1toOut2 = true;
                    DFR22Unit_MixPointChanged();
                }
                else if (responce.Contains("MIX001002C000"))
                {
                    MixPointCh1toOut2 = false;
                    DFR22Unit_MixPointChanged();
                }
                else if (responce.Contains("MIX002001C001"))
                {
                    MixPointCh2toOut1 = true;
                    DFR22Unit_MixPointChanged();
                }
                else if (responce.Contains("MIX002001C000"))
                {
                    MixPointCh2toOut1 = false;
                    DFR22Unit_MixPointChanged();
                }
                else if (responce.Contains("MIX002002C001"))
                {
                    MixPointCh2toOut2 = true;
                    DFR22Unit_MixPointChanged();
                }
                else if (responce.Contains("MIX002002C000"))
                {
                    MixPointCh2toOut2 = false;
                    DFR22Unit_MixPointChanged();
                }
                Debug(string.Format("Get MixPoint change responce: Mix In1 to Out1 {0}, Mix In1 to Out2 {1}, Mix In2 to Out1 {2}, Mix In2 to Out2 {3}", MixPointCh1toOut1.ToString(), MixPointCh1toOut2.ToString(), MixPointCh2toOut1.ToString(), MixPointCh2toOut2.ToString()), ErrorLevel.None);

            }

        }

        #region TextProtocol CMD

        private double DFRToVol(int VolFromDFR)
        {
            double Vol = 0;
            if (VolFromDFR > 0 && VolFromDFR < 27)
                Vol = 2.5 * VolFromDFR - 107.5;
            else if (VolFromDFR > 26)
                Vol = 0.5 * VolFromDFR - 53.5;
            else
                Vol = -107.5;

            return Vol;
        }
        private int VolToDFR(double Vol)
        {
            double DFRVolume = 0;
            if (Vol < -40)
                DFRVolume = (Vol + 105) / 2.5 + 1;
            else if (Vol >= -40 && Vol <= 10)
                DFRVolume = (Vol + 40) / 0.5 + 27;
            else
                DFRVolume = 0;
            return Convert.ToInt16(DFRVolume);
        }
        private byte[] MixPointVolUp(int Input, int Output)
        {
            if (Input > 0 && Input < 3 && Output > 0 && Output < 3)
            {
                switch (Input)
                {
                    case 1:
                        switch (Output)
                        {
                            case 1:
                                SendMessage = "DFR22" + DevID + "MIX001001I001";

                                break;
                            case 2:
                                SendMessage = "DFR22" + DevID + "MIX001002I001";

                                break;
                            default:
                                break;
                        }

                        break;
                    case 2:
                        switch (Output)
                        {
                            case 1:
                                SendMessage = "DFR22" + DevID + "MIX002001I001";

                                break;
                            case 2:
                                SendMessage = "DFR22" + DevID + "MIX002002I001";

                                break;
                            default:
                                break;
                        }
                        break;
                    default:
                        break;
                }
                var MSG = StringFormatting(SendMessage);
                return MSG;
            }
            return null;
        }
        private byte[] MixPointVolDown(int Input, int Output)
        {
            if (Input > 0 && Input < 3 && Output > 0 && Output < 3)
            {
                switch (Input)
                {
                    case 1:
                        switch (Output)
                        {
                            case 1:
                                SendMessage = "DFR22" + DevID + "MIX001001D001";

                                break;
                            case 2:
                                SendMessage = "DFR22" + DevID + "MIX001002D001";

                                break;
                            default:
                                break;
                        }

                        break;
                    case 2:
                        switch (Output)
                        {
                            case 1:
                                SendMessage = "DFR22" + DevID + "MIX002001D001";

                                break;
                            case 2:
                                SendMessage = "DFR22" + DevID + "MIX002002D001";

                                break;
                            default:
                                break;
                        }
                        break;
                    default:
                        break;
                }
                var MSG = StringFormatting(SendMessage);
                return MSG;
            }
            return null;
        }
        private byte[] SetMixPointVolumeAt(int Input, int Output, double Volume)
        {
            if (Input > 0 && Input < 3 && Output > 0 && Output < 3)
            {
                switch (Input)
                {
                    case 1:
                        switch (Output)
                        {
                            case 1:
                                SendMessage = "DFR22" + DevID + "MIX001001L00" + Convert.ToChar(VolToDFR(Volume));

                                break;
                            case 2:
                                SendMessage = "DFR22" + DevID + "MIX001002L00" + Convert.ToChar(VolToDFR(Volume));

                                break;
                            default:
                                break;
                        }

                        break;
                    case 2:
                        switch (Output)
                        {
                            case 1:
                                SendMessage = "DFR22" + DevID + "MIX002001L00" + Convert.ToChar(VolToDFR(Volume));

                                break;
                            case 2:
                                SendMessage = "DFR22" + DevID + "MIX002002L00" + Convert.ToChar(VolToDFR(Volume));

                                break;
                            default:
                                break;
                        }
                        break;
                    default:
                        break;
                }
                var MSG = StringFormatting(SendMessage);
                return MSG;
            }
            return null;
        }
        private byte[] QueryStatus()
        {
            SendMessage = "DFR22" + DevID + "QRY";
            var MSG = StringFormatting(SendMessage);
            return MSG;
        }
        private byte[] SetVolumeAt(int ChanelNum, double Volume)
        {
            if (ChanelNum > 0 && ChanelNum < 5)
            {
                switch (ChanelNum)
                {
                    case 1:
                        SendMessage = "DFR22" + DevID + "INP001L00" + Convert.ToChar(VolToDFR(Volume));

                        break;
                    case 2:
                        SendMessage = "DFR22" + DevID + "INP002L00" + Convert.ToChar(VolToDFR(Volume));

                        break;
                    case 3:
                        SendMessage = "DFR22" + DevID + "OUT001L00" + Convert.ToChar(VolToDFR(Volume));

                        break;
                    case 4:
                        SendMessage = "DFR22" + DevID + "OUT002L00" + Convert.ToChar(VolToDFR(Volume));

                        break;
                    default:
                        break;
                }
                var MSG = StringFormatting(SendMessage);
                return MSG;
            }
            return null;
        }
        private byte[] StringFormatting(string DataString)
        {
            byte[] _Prefix = { 0xD0 };
            byte[] _Syffix = { 0xD1 };
            byte[] CRLF = { 0x0D, 0x0A };
            byte[] CMDtoDFR22;

            byte[] Body = Encoding.ASCII.GetBytes(DataString);
            CMDtoDFR22 = new byte[_Prefix.Length + Body.Length + _Syffix.Length + CRLF.Length];
            _Prefix.CopyTo(CMDtoDFR22, 0);
            Body.CopyTo(CMDtoDFR22, _Prefix.Length);
            _Syffix.CopyTo(CMDtoDFR22, _Prefix.Length + Body.Length);
            CRLF.CopyTo(CMDtoDFR22, _Prefix.Length + Body.Length + _Syffix.Length);
            return CMDtoDFR22;
        }
        private byte[] FaderUp(int ChanelNum)
        {
            if (ChanelNum > 0 && ChanelNum < 3)
            {
                switch (ChanelNum)
                {
                    case 1:
                        SendMessage = "DFR22" + DevID + "MIX001OUTI001";

                        break;
                    case 2:
                        SendMessage = "DFR22" + DevID + "MIX002OUTI001";

                        break;
                    default:
                        break;
                }
                var MSG = StringFormatting(SendMessage);
                return MSG;
            }
            return null;
        }
        private byte[] FaderDown(int ChanelNum)
        {
            if (ChanelNum > 0 && ChanelNum < 3)
            {
                switch (ChanelNum)
                {
                    case 1:
                        SendMessage = "DFR22" + DevID + "MIX001OUTD001";

                        break;
                    case 2:
                        SendMessage = "DFR22" + DevID + "MIX002OUTD001";

                        break;
                    default:
                        break;
                }
                var MSG = StringFormatting(SendMessage);
                return MSG;
            }
            return null;
        }
        private byte[] VolumeUP(int ChanelNum)
        {
            if (ChanelNum > 0 && ChanelNum < 5)
            {
                switch (ChanelNum)
                {
                    case 1:
                        SendMessage = "DFR22" + DevID + "INP001I001";

                        break;
                    case 2:
                        SendMessage = "DFR22" + DevID + "INP002I001";

                        break;
                    case 3:
                        SendMessage = "DFR22" + DevID + "OUT001I001";

                        break;
                    case 4:
                        SendMessage = "DFR22" + DevID + "OUT002I001";

                        break;
                    default:
                        break;
                }
                var MSG = StringFormatting(SendMessage);
                return MSG;
            }
            return null;
        }
        private byte[] VolumeDown(int ChanelNum)
        {
            if (ChanelNum > 0 && ChanelNum < 5)
            {
                switch (ChanelNum)
                {
                    case 1:
                        SendMessage = "DFR22" + DevID + "INP001D001";

                        break;
                    case 2:
                        SendMessage = "DFR22" + DevID + "INP002D001";

                        break;
                    case 3:
                        SendMessage = "DFR22" + DevID + "OUT001D001";

                        break;
                    case 4:
                        SendMessage = "DFR22" + DevID + "OUT002D001";

                        break;
                    default:
                        break;
                }
                var MSG = StringFormatting(SendMessage);
                return MSG;
            }
            return null;
        }
        private byte[] InOutMute(int ChanelNum, bool State)
        {
            if (ChanelNum > 0 && ChanelNum < 5)
            {
                switch (ChanelNum)
                {
                    case 1:
                        if (State)
                            SendMessage = "DFR22" + DevID + "INP001M001";
                        else
                            SendMessage = "DFR22" + DevID + "INP001M000";

                        break;
                    case 2:
                        if (State)
                            SendMessage = "DFR22" + DevID + "INP0022M001";
                        else
                            SendMessage = "DFR22" + DevID + "INP002M000";

                        break;
                    case 3:
                        if (State)
                            SendMessage = "DFR22" + DevID + "OUT001M001";
                        else
                            SendMessage = "DFR22" + DevID + "OUT001M000";

                        break;
                    case 4:
                        if (State)
                            SendMessage = "DFR22" + DevID + "OUT002M001";
                        else
                            SendMessage = "DFR22" + DevID + "OUT002M000";

                        break;
                    default:
                        break;
                }
                var MSG = StringFormatting(SendMessage);
                return MSG;
            }
            return null;
        }
        private byte[] InOutMuteToggle(int ChanelNum)
        {
            if (ChanelNum > 0 && ChanelNum < 5)
            {
                switch (ChanelNum)
                {
                    case 1:
                        SendMessage = "DFR22" + DevID + "INP001M002";

                        break;
                    case 2:
                        SendMessage = "DFR22" + DevID + "INP002M002";

                        break;
                    case 3:
                        SendMessage = "DFR22" + DevID + "OUT001M002";

                        break;
                    case 4:
                        SendMessage = "DFR22" + DevID + "OUT002M002";

                        break;
                    default:
                        break;
                }
                var MSG = StringFormatting(SendMessage);
                return MSG;
            }
            return null;
        }
        private byte[] SetMixPoint(int Input, int Output, bool MixPointIsOn)
        {
            if (Input > 0 && Input < 3 && Output > 0 && Output < 3)
            {
                switch (Input)
                {
                    case 1:
                        switch (Output)
                        {
                            case 1:
                                if (MixPointIsOn)
                                    SendMessage = "DFR22" + DevID + "MIX001001C001";
                                else
                                    SendMessage = "DFR22" + DevID + "MIX001001C000";

                                break;
                            case 2:
                                if (MixPointIsOn)
                                    SendMessage = "DFR22" + DevID + "MIX001002C001";
                                else
                                    SendMessage = "DFR22" + DevID + "MIX001002C000";

                                break;
                            default:
                                break;
                        }
                        break;
                    case 2:
                        switch (Output)
                        {
                            case 1:
                                if (MixPointIsOn)
                                    SendMessage = "DFR22" + DevID + "MIX002001C001";
                                else
                                    SendMessage = "DFR22" + DevID + "MIX002001C000";

                                break;
                            case 2:
                                if (MixPointIsOn)
                                    SendMessage = "DFR22" + DevID + "MIX002002C001";
                                else
                                    SendMessage = "DFR22" + DevID + "MIX002002C000";

                                break;
                            default:
                                break;
                        }
                        break;
                    default:
                        break;
                }
                var MSG = StringFormatting(SendMessage);
                return MSG;
            }
            return null;
        }
        #endregion
    }

}



