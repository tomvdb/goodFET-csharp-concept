using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using System.IO.Ports;
using System.Threading;

namespace GoodFETTest
{
  public partial class Form1 : Form
  {
    private List<byte> buffer = new List<byte>();
    private bool deviceStarted = false;

    private SerialPort comport = new SerialPort();

    private static byte lastApp = 0;
    private static byte lastVerb = 0;
    private static byte[] lastData = new byte[0];
    private static bool receivedFlag = false;

    public Form1()
    {
      InitializeComponent();
    }

    private delegate void debugDelegate(ListBox listBoxToUpdate, object data);
    public void debug_(ListBox listBoxToUpdate, object data)
    {
      if (listBoxToUpdate.InvokeRequired)
      {
        debugDelegate dd = new debugDelegate(debug_);
        listBoxToUpdate.Invoke(dd, new object[] { listBoxToUpdate, data });
      }
      else
      {
        int i = listBoxToUpdate.Items.Add(DateTime.Now.ToShortTimeString() + " : " + data);
        listBoxToUpdate.TopIndex = i;
      }
    }

    public void debug(object data)
    {
      debug_(listBox1, data);
    }

    // connect
    private void button1_Click(object sender, EventArgs e)
    {
      if (comport.IsOpen)
        comport.Close();

      // reset
      buffer.Clear();
      deviceStarted = false;

      debug("Connecting to " + textBox1.Text);

      comport.DataBits = 8;
      comport.StopBits = StopBits.One;
      comport.BaudRate = 115200;
      comport.Parity = Parity.None;
      comport.PortName = textBox1.Text;
      comport.DtrEnable = true;
      comport.RtsEnable = true;
      comport.DataReceived += new SerialDataReceivedEventHandler(comport_DataReceived);
      comport.Open();

      ResetDevice(false);

      debug("Success!");
    }

    byte[] writeCmd(byte app, byte verb, byte[] data )
    {
      if (!comport.IsOpen)
        return null;

      Int16 datalen = 0;

      if (data != null)
        datalen = (Int16)data.Count();

      byte[] frame = new byte[datalen + 4];

      receivedFlag = false;

      frame[0] = app;
      frame[1] = verb;
      frame[2] = (byte)(datalen);  
      frame[3] = (byte)(datalen >> 8);

      if (datalen > 0)
      {
        Buffer.BlockCopy(data, 0, frame, 4, datalen);
      }

      comport.Write(frame, 0, datalen + 4);

      int timeout = 60;
      int s = DateTime.Now.Second;

      while (receivedFlag != true && timeout > 0)
      {
        Application.DoEvents();
        
        if (s != DateTime.Now.Second )
          timeout--;
      }

      if (timeout == 0)
      {
        debug("timed out");
        return null;
      }

      if (lastData.Count() > 0)
      {
        byte[] resultData = new byte[lastData.Count()];
        Buffer.BlockCopy(lastData, 0, resultData, 0, lastData.Count());
        return resultData;
      }

      return null;
    }

    void comport_DataReceived(object sender, SerialDataReceivedEventArgs e)
    {
      //string dataReceived = comport.ReadExisting();
      //debug("Read : " + dataReceived.Length);

      while (comport.BytesToRead > 0)
      {
        byte data = (byte)comport.ReadByte();
        buffer.Add(data);
      }

      if ( buffer.Count > 0 )
        processData();
    }

    void processData()
    {
      lock (buffer)
      {

        // do we have atleast enough data to read a 'header' ?
        // u8 app, u8 verb, u16 count

        if (buffer.Count < 4)
        {
          debug("Not Enough Data!!");
          return;
        }

        byte frameApp = buffer[0];
        byte frameVerb = buffer[1];

        debug("app : " + frameApp.ToString("X"));
        debug("verb : " + frameVerb.ToString("X"));

        if (!deviceStarted)
        {
          if (frameApp != 0x00 || frameVerb != 0x7f)
          {
            // device didn't start properly - it happens
            debug("device didn't sync properly - reset");
            buffer.Clear();
            ResetDevice(true);
            return;
          }
          else
          {
            debug("device has synced");
            deviceStarted = true;
          }
        }

        // little endian
        int frameCount = (short)(buffer[3] << 8 | buffer[2]);

        debug("count : " + frameCount);

        // do we have enough data for the complete frame ?
        if (buffer.Count < (4 + frameCount))
          return;

        // read data here
        byte[] frameData = new byte[frameCount];
        Buffer.BlockCopy(buffer.ToArray(), 4, frameData, 0, frameCount);

        // remove read data
        // todo : range checking!!
        buffer.RemoveRange(0, (4 + frameCount));

        // handle data
        lastApp = frameApp;
        lastVerb = frameVerb;
        lastData = frameData;
        receivedFlag = true;

        handleData(frameApp, frameVerb, frameData);

        debug("done");
      }
    }

    private void handleData(byte frameApp, byte frameVerb, byte[] frameData)
    {
      switch (frameApp)
      {

        case 0x00: switch (frameVerb)
          {
            case 0x02: break;
            case 0x7f: debug( ASCIIEncoding.ASCII.GetString(frameData) ); break;
            default: debug("Unknown Command for app 0x00 - " + frameVerb.ToString("X")); break;
          }
          break;

        case 0xFF: debug("DEBUG " + ASCIIEncoding.ASCII.GetString(frameData)); break;

        default: debug("unhandled app " + frameApp.ToString("x")); break;
      }
    }

    private void button2_Click(object sender, EventArgs e)
    {
      comport.Close();
    }

    private void ResetDevice( bool reset )
    {
      if (reset)
      {
        comport.DtrEnable = true;
        comport.RtsEnable = true;
        Thread.Sleep(200);
      }

      Thread.Sleep(500);
      comport.DtrEnable = false;
      comport.RtsEnable = false;
      Thread.Sleep(200);
    }

    // goodfet.monitor info
    private void button3_Click(object sender, EventArgs e)
    {
      try
      {
        byte a = peek8(0x00, 0xff0);
        byte b = peek8(0x00, 0xff1);

        debug("GoodFET with " + a.ToString("X") + b.ToString("X") + " MCU");

        a = peek8(0x00, 0x56);
        b = peek8(0x00, 0x57);

        debug("Clocked at " + a.ToString("X") + b.ToString("X"));
      }
      catch (Exception)
      {
        debug("Error reading Info");
      }
    }

    private byte peek8(byte app, Int16 address)
    {
      byte[] addr = new byte[2];

      addr[0] = (byte)address;
      addr[1] = (byte)(address >> 8);

      try
      {
        byte a = writeCmd(app, 0x02, addr)[0];
        return a;
      }
      catch (Exception Ex)
      {
        throw new Exception("Error Reading Value - Peek8");
      }

    }

    private void button4_Click(object sender, EventArgs e)
    {
      byte[] data;

      data = writeCmd(0x00, 0xD0, null);
    }


  }
}
