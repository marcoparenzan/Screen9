using System.Globalization;
using QBSharp.Core.Syntax;

namespace QBSharp.Core.Runtime;

internal static class Values
{
    public static Type ResolveType(string name) => name.ToUpperInvariant() switch
    {
        "INTEGER" => typeof(int), "LONG" => typeof(long), "DOUBLE" => typeof(double), "DECIMAL" => typeof(decimal),
        "BOOLEAN" => typeof(bool), "STRING" => typeof(string), "OBJECT" => typeof(object), "DATE" => typeof(DateTime),
        _ => throw new InvalidOperationException($"Unknown type '{name}'.")
    };
    public static object? Default(Type? type) => type is not null && type.IsValueType ? Activator.CreateInstance(type) : null;
    public static bool Numeric(object? value) => value is byte or sbyte or short or ushort or int or uint or long or float or double or decimal;
    public static bool Boolean(object? value) => value is bool b ? b : throw new InvalidOperationException("Expected Boolean (no truthiness coercion).");
    public static string Text(object? value) => Convert.ToString(value, CultureInfo.InvariantCulture) ?? "";
    public static object? ConvertTo(object? value, Type? type)
    {
        if (type is null || type == typeof(object)) return value;
        if (value is null)
        {
            if (!type.IsValueType || Nullable.GetUnderlyingType(type) is not null) return null;
            throw new InvalidOperationException($"Nothing cannot be assigned to {type.Name}.");
        }
        if (type.IsInstanceOfType(value)) return value;
        var target = Nullable.GetUnderlyingType(type) ?? type;
        if (Numeric(value) && (target == typeof(int) || target == typeof(long) || target == typeof(double) || target == typeof(decimal) || target == typeof(float) || target == typeof(short) || target == typeof(byte)))
        {
            if (target == typeof(int) || target == typeof(long) || target == typeof(short) || target == typeof(byte))
            {
                var number = Convert.ToDecimal(value, CultureInfo.InvariantCulture);
                if (decimal.Truncate(number) != number) throw new InvalidOperationException("Fractional value cannot be assigned to integral type.");
            }
            var converted = Convert.ChangeType(value, target, CultureInfo.InvariantCulture);
            if (converted is double d && !double.IsFinite(d) || converted is float f && !float.IsFinite(f)) throw new OverflowException();
            return converted;
        }
        throw new InvalidOperationException($"Cannot convert {value.GetType().Name} to {type.Name}.");
    }
    public static object Unary(UnaryOperator op, object? value)
    {
        if (op == UnaryOperator.Not) return !Boolean(value);
        if (!Numeric(value)) throw new InvalidOperationException("Expected numeric operand.");
        if (op == UnaryOperator.Plus) return value!;
        return value switch
        {
            double d => Finite(-d), float f => Finite(-f), decimal d => -d,
            long n => checked(-n), _ => checked(-Convert.ToInt32(value, CultureInfo.InvariantCulture))
        };
    }
    private static double Finite(double value) => double.IsFinite(value) ? value : throw new OverflowException("Non-finite numeric result.");
    public static object Binary(BinaryOperator op, object? left, object? right)
    {
        if (op == BinaryOperator.Concatenate) return Text(left) + Text(right);
        if (op is BinaryOperator.And or BinaryOperator.AndAlso) return Boolean(left) & Boolean(right);
        if (op is BinaryOperator.Or or BinaryOperator.OrElse) return Boolean(left) | Boolean(right);
        if (op is BinaryOperator.Equal or BinaryOperator.NotEqual)
        {
            bool equal = Numeric(left) && Numeric(right) ? Compare(left!, right!) == 0 : Equals(left, right);
            return op == BinaryOperator.Equal ? equal : !equal;
        }
        if (op is BinaryOperator.Less or BinaryOperator.LessOrEqual or BinaryOperator.Greater or BinaryOperator.GreaterOrEqual)
        {
            int compare = Compare(left!, right!);
            return op switch { BinaryOperator.Less => compare < 0, BinaryOperator.LessOrEqual => compare <= 0, BinaryOperator.Greater => compare > 0, _ => compare >= 0 };
        }
        if (!Numeric(left) || !Numeric(right)) throw new InvalidOperationException("Expected numeric operands; use '&' for text concatenation.");
        if (op is BinaryOperator.Power or BinaryOperator.Divide)
        {
            var a = Convert.ToDouble(left, CultureInfo.InvariantCulture); var b = Convert.ToDouble(right, CultureInfo.InvariantCulture);
            if (op == BinaryOperator.Divide && b == 0) throw new DivideByZeroException();
            return Finite(op == BinaryOperator.Power ? Math.Pow(a, b) : a / b);
        }
        if (op == BinaryOperator.IntegerDivide)
        {
            if (left is not (int or long) || right is not (int or long)) throw new InvalidOperationException("Integer division requires Integer or Long operands.");
            var result = checked(Convert.ToInt64(left) / Convert.ToInt64(right));
            return left is int && right is int ? (object)checked((int)result) : result;
        }
        if (left is double or float || right is double or float)
        {
            double a = Convert.ToDouble(left, CultureInfo.InvariantCulture), b = Convert.ToDouble(right, CultureInfo.InvariantCulture);
            if (op == BinaryOperator.Modulo && b == 0) throw new DivideByZeroException();
            return Finite(op switch { BinaryOperator.Add => a + b, BinaryOperator.Subtract => a - b, BinaryOperator.Multiply => a * b, BinaryOperator.Modulo => a % b, _ => throw new InvalidOperationException() });
        }
        if (left is decimal || right is decimal)
        {
            decimal a = Convert.ToDecimal(left, CultureInfo.InvariantCulture), b = Convert.ToDecimal(right, CultureInfo.InvariantCulture);
            return op switch { BinaryOperator.Add => a + b, BinaryOperator.Subtract => a - b, BinaryOperator.Multiply => a * b, BinaryOperator.Modulo => a % b, _ => throw new InvalidOperationException() };
        }
        long x = Convert.ToInt64(left, CultureInfo.InvariantCulture), y = Convert.ToInt64(right, CultureInfo.InvariantCulture);
        long n = op switch { BinaryOperator.Add => checked(x + y), BinaryOperator.Subtract => checked(x - y), BinaryOperator.Multiply => checked(x * y), BinaryOperator.Modulo => x % y, _ => throw new InvalidOperationException() };
        return left is long || right is long ? n : (object)checked((int)n);
    }
    private static int Compare(object left, object right)
    {
        if (Numeric(left) && Numeric(right)) return left is double or float || right is double or float
            ? Convert.ToDouble(left, CultureInfo.InvariantCulture).CompareTo(Convert.ToDouble(right, CultureInfo.InvariantCulture))
            : Convert.ToDecimal(left, CultureInfo.InvariantCulture).CompareTo(Convert.ToDecimal(right, CultureInfo.InvariantCulture));
        if (left is string a && right is string b) return string.CompareOrdinal(a, b);
        if (left is DateTime date && right is DateTime other) return date.CompareTo(other);
        throw new InvalidOperationException("Operands are not comparable.");
    }
}
