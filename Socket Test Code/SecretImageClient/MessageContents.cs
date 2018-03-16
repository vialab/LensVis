using System;

namespace SecretImageClient
{
    public class MessageContent
    {
        public char _ContentType;
        public int _MessageSize;
        public byte[] _MessageSizeBytes;
        public byte[] _ContentBytes;

        public char ContentType
        {
            get
            {
                return _ContentType;
            }

            set
            {
                _ContentType = value;
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

        public byte[] ContentBytes
        {
            get
            {
                return _ContentBytes;
            }

            set
            {
                _ContentBytes = value;
            }
        }

        public MessageContent()
        {
           
        }
    }
}
