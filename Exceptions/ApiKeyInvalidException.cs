﻿using System;
using System.Runtime.Serialization;

namespace Hspi.Exceptions
{
    [Serializable]
    internal class ApiKeyInvalidException : WUWeatherDataInvalidException
    {
        public ApiKeyInvalidException() : base("Invalid API Key")
        {
        }

        protected ApiKeyInvalidException(SerializationInfo info, StreamingContext context) : base(info, context)
        {
        }
    }
}