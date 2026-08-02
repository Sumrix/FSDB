using System;

namespace FolderDB.Infrastructure.Exceptions;

public class RecordConversionException(string message, Exception? inner = null) : Exception(message, inner);