namespace TechChallenge.Oficina.Controllers.Features.Orcamentos
{
    public sealed class OrcamentosResult<TValue, TError>
    {
        public TValue? Value { get; set; }
        public TError? Error { get; set; }

        public OrcamentosResult() { }

        public OrcamentosResult(TValue result)
        {
            Value = result;
            Error = default;
        }

        public OrcamentosResult(TError error)
        {
            Value = default;
            Error = error;
        }
    }

    public static class OrcamentosResult
    {
        public static OrcamentosResult<TValue, Exception> From<TValue>(TValue result)
            => new(result);

        public static OrcamentosResult<TValue, Exception> FromError<TValue>(Exception error)
            => new(error);
    }
}
