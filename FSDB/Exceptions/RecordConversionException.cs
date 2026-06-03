using System;

namespace FSDB.Exceptions;

public class RecordConversionException(string message, Exception? inner = null) : Exception(message, inner);