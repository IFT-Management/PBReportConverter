﻿using System.Text;
using System.Text.RegularExpressions;

namespace PBReportConverter.Parser;

internal class PBExpressionParser
{
    private StringReader? _reader;
    private int _lastChar;
    private char _lastCharAsChar;
    private readonly StringBuilder _expressionSb = new();
    private readonly StringBuilder _expressionBufferSb = new();
    private readonly StringBuilder _lastStrBufferSb = new();
    private bool _writeToBuffer = false;
    private static readonly List<string> _knownKeywords = ["when", "then", "and", "or", "else", "not"];
    private static readonly List<char> _exprDelims = ['(', ')', ',', '\''];
    private string _event = "BeforePrint";
    internal static readonly string[] sourceArray = ["left", "right"];

    private static string FormatChar(int c) => c < 32 ? $"\\x{c:X2}" : $"'{(char)c}'";

    private void ReadChar()
    {
        _lastChar = _reader!.Read();
        _lastCharAsChar = (char) _lastChar;
    }

    private void Append(string value)
    {
        if (_writeToBuffer)
        {
            _expressionBufferSb.Append(value);
        }
        else
        {
            _expressionSb.Append(value);
        }
    }

    private void Append(char value)
    {
        Append(value.ToString());
    }

    private void AddChar(bool skipWhitespace = true)
    {
        Append(_lastCharAsChar);
        if(_lastChar == '=')
        {
            Append('=');
        }
        if (skipWhitespace)
        {
            ReadCharSkipWhitespace();
        }
        else
        {
            ReadChar();
        }
    }

    private void ReadNonAlphanumericChar()
    {
        while (_lastChar >= 0 && !(char.IsAsciiLetterOrDigit(_lastCharAsChar) || _exprDelims.Contains(_lastCharAsChar)))
        {
            AddChar();
        }
    }

    private void ReadCharSkipWhitespace()
    {
        do ReadChar();
        while (_lastChar >= 0 && char.IsWhiteSpace(_lastCharAsChar));
    }

    private string ParseString()
    {
        Span<char> buf = stackalloc char[10000];
        int pos = 0;
        if (Char.IsWhiteSpace(_lastCharAsChar))
        {
            ReadCharSkipWhitespace();
        }
        while (Char.IsAsciiLetterOrDigit(_lastCharAsChar) || _lastChar == '_')
        {
            buf[pos++] = _lastCharAsChar;
            ReadChar();
        }
        if (Char.IsWhiteSpace(_lastCharAsChar))
        {
            ReadCharSkipWhitespace();
        }

        if (pos == 0) 
            throw new Exception($"Unexpected character {FormatChar(_lastChar)}.");

        return buf[..pos].ToString();
    }

    public (string printEvent, string expr) Parse(string expression)
    {
        _expressionSb.Clear();
        expression = Regex.Replace(expression, @"\sfor [a-zA-Z0-9]+(\s[0-9]+)?", "");
        _reader = new StringReader(expression);
        _event = "BeforePrint";
        ReadChar();
        ParseExpression();

        _reader.Dispose();

        return (_event, _expressionSb.ToString().Replace("<>", "!=").Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;"));
    }

    private void ParseBracketExpression()
    {
        if(_lastChar < 0)
        {
            return;
        }
        if (_lastChar == '(')
        {
            AddChar();
            ParseExpression();
            if (_lastChar == ')')
            {
                AddChar();
                ReadNonAlphanumericChar();
            }
        }
    }

    public void ParseExpression()
    {
        for (; ; )
        {
            if (!(_lastChar >= 0 && _lastChar != ')' && _lastChar != ','))
            {
                break;
            }
            else if (_lastChar == '/' && _reader!.Peek() == '*')
            {
                ReadChar();
                ParseComment();
                continue;
            }
            else
            {
                ReadNonAlphanumericChar();
            }

            if (_lastChar == '(')
            {
                ParseBracketExpression();
                continue;
            }
            else if (_lastChar == '\'' || Char.IsAsciiDigit(_lastCharAsChar))
            {
                ParseLiteral();
            }
            else
            {
                var str = ParseString();

                if (_knownKeywords.Contains(str.ToLower()))
                {
                    var mapOp = MapOperation(str.ToLower());
                    if (mapOp != string.Empty)
                    {
                        Append(mapOp);
                        continue;

                    }
                    _lastStrBufferSb.Clear();
                    _lastStrBufferSb.Append(str.ToLower());
                    break;
                }
                if (_lastChar == '(')
                {
                    Append(FunctionHelper(str));
                    AddChar();
                    switch (str.ToLower()){
                        case "case": { ParseCaseParameter(); break; }
                        case "string":
                        case "pos": { FlipParameters(str); break; }
                        case "page":
                        case "pagecount":
                        {
                            _event = "PrintOnPage";
                            SkipParameters();
                            break;
                        }
                        case "last":
                        case "cumulativesum":
                        {
                            SkipParameters();
                            break;
                        }
                        default:
                        {
                            ParseParameters(str);
                            if (str.Equals("right", StringComparison.CurrentCultureIgnoreCase))
                            {
                                Append(')');
                            }
                            else if (str.Equals("getrow", StringComparison.CurrentCultureIgnoreCase))
                            {
                                Append("+1");
                            }
                            break;
                        }
                    }
                }
                else
                {
                    Append(str);
                }
            }
        }
    }

    private static string MapOperation(string op)
    {
        return op switch
        {
            "and" => " && ",
            "or" => " || ",
            "not" => "!",
            _ => ""
        };
    }

    private void SkipParameters()
    {
        _expressionSb.Length -= 1;
        var bracketPairCheck = 0;
        while(_lastChar >= 0)
        {
            if (_lastChar == ')')
            {
                if (bracketPairCheck == 0)
                {
                    ReadCharSkipWhitespace();
                    break;
                }
                bracketPairCheck--;
            }
            else if (_lastChar == '(')
            {
                bracketPairCheck++;
            }
            ReadChar();
        }
    }

    private void ParseComment()
    {
        ReadChar();
        while(_lastChar >= 0 && _lastChar != '*')
        {
            ReadCharSkipWhitespace();
            if (_lastChar == '*' && _reader!.Peek() == '/')
            {
                ReadChar();
                break;
            }
        }
        if( _lastChar != '/')
        {
            throw new Exception($"Unexpected character while parsing comment: {FormatChar(_lastChar)}.");
        }
        ReadChar();
    }

    private void ParseLiteral()
    {
        if(_lastChar == '\'')
        {
            AddChar(false);
            while(_lastChar >=0 && _lastChar != '\'')
            {
                AddChar(false);
            }
            if (_lastChar == '\'')
            {
                AddChar();
            }
            else
            {
                throw new Exception($"Unexpected character {FormatChar(_lastChar)}.");
            }
        }
        else
        {
            while (Char.IsAsciiDigit(_lastCharAsChar)){
                AddChar();
            }
        }
    }

    private void ParseParameters(string function)
    {
        if(function.Equals("right", StringComparison.CurrentCultureIgnoreCase))
        {
            Append("Reverse(");
            ParseExpression();
            Append(')');
        }
        else if(function.Equals("left", StringComparison.CurrentCultureIgnoreCase))
        {
            ParseExpression();
        }

        if (sourceArray.Contains(function.ToLower()))
        {
            Append(",0");
        }

        for(; ; )
        {
            if (_lastChar == ',')
            {
                AddChar();
            }
            else if (_lastChar == ')' || _lastChar < 0)
            {
                AddChar();
                break;
            }
            ParseExpression();
        }
    }

    private void ParseCaseParameter()
    {
        var elseCheck = false;
        _writeToBuffer = true;
        ParseExpression();
        var expressionToCheck = _expressionBufferSb.ToString();
        _expressionBufferSb.Clear();
        _writeToBuffer = false;
        for (; ; )
        {
            if (_lastChar == ')' || _lastChar < 0)
            {
                if (!elseCheck)
                {
                    Append("''");
                }
                AddChar();
                break;
            }
            if (_lastStrBufferSb.ToString().Equals("else", StringComparison.CurrentCultureIgnoreCase))
            {
                ParseExpression();
                elseCheck = true;
                continue;
            }
            Append(expressionToCheck + "==");
            ParseExpression();
            Append(',');
            ParseExpression();
            Append(',');
        }
    }

    private void FlipParameters(string function)
    {
        _writeToBuffer = true;
        ParseExpression();
        var firstParam = _expressionBufferSb.ToString();
        _expressionBufferSb.Clear();
        ReadCharSkipWhitespace();
        ParseExpression();
        var secondParam = _expressionBufferSb.ToString();
        _expressionBufferSb.Clear();
        _writeToBuffer = false;
        if (_lastChar == ')' || _lastChar < 0)
        {
            Append($"{secondParam}{(function.Equals("string", StringComparison.CurrentCultureIgnoreCase) ? ",0" : "")},{firstParam}");
            AddChar();
        }
        else
        {
            throw new Exception($"Unexpected character {FormatChar(_lastChar)}.");
        }
    }

    private static string FunctionHelper(string function)
    {
        return function.ToLower() switch
        {
            "if" => "Iif",
            "sum" => "[].Sum",
            "cumulativesum" => "1",
            "date" => "GetDate",
            "left" or "mid" or "string" => "Substring",
            "round" => "Round",
            "right" => "Reverse(Substring",
            "isnull" => "IsNull",
            "today" => "Today",
            "case" => "Iif",
            "pos" => "CharIndex",
            "getrow" => "CurrentRowIndexInGroup",
            "avg" => "[].Avg",
            "len" => "Len",
            "abs" => "Abs",
            "max" => "[].Max",
            "min" => "[].Min",
            "last" => "[DataSource.RowCount]",
            "page" => "[Arguments.PageIndex]",
            "pagecount" => "[Arguments.PageCount]",
            "trim" or "lefttrim" or "righttrim" => "Trim",
            _ => throw new Exception($"Unrecognized function: {function}")
        };
    }
}
