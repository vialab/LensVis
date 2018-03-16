using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SecretImageClient
{
  public class MessageContent
  {
    private char _ContentType;
    private byte[] _ContentTypeBytes;
    private int _MessageSize = 0;
    private byte[] _MessageSizeBytes;
    private byte[] _MessageBytes;

    public char ContentType
    {
      get
      {
        return _ContentType;
      }

      set
      {
        _ContentType = value;
        ContentTypeBytes = new byte[] { Convert.ToByte(_ContentType) };
      }
    }

    public byte[] ContentTypeBytes
    {
      get
      {
        return _ContentTypeBytes;
      }

      set
      {
        _ContentTypeBytes = value;
      }
    }

    public int MessageSize
    {
      get
      {
        return _MessageSize;
      }

      set
      {
        _MessageSize = value;
        _MessageSizeBytes = BitConverter.GetBytes(_MessageSize);
      }
    }

    public byte[] MessageSizeBytes
    {
      get
      {
        return _MessageSizeBytes;
      }

      set
      {
        _MessageSizeBytes = value;
      }
    }

    public byte[] MessageBytes
    {
      get
      {
        return _MessageBytes;
      }

      set
      {
        _MessageBytes = value;
        MessageSize = _MessageBytes.Length;
      }
    }



    public MessageContent()
    {

    }
  }
}
