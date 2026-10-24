namespace Astra.Core;

public abstract class Option<T>
{
    private Option() { }
    public sealed class Some : Option<T>
    {
        public T Value { get; }
        public Some(T value)
        {
            ArgumentNullException.ThrowIfNull(value);
            Value = value;
        }
    }
    public sealed class None : Option<T> { }
}
