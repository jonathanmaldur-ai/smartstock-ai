using SmartStock.Application.Common;
using SmartStock.Domain.Auditing;
using SmartStock.Domain.Users;

namespace SmartStock.Tests.Domain;

public sealed class DomainTests
{
    [Fact]
    public void Existem_exatamente_os_quatro_perfis_do_PRD()
    {
        Assert.Equal(["Administrador", "Gerente", "Operador", "Consulta"], Roles.All);
        Assert.False(Roles.IsValid("administrador"));
    }

    [Fact]
    public void Registro_de_auditoria_exige_acao()
    {
        Assert.Throws<ArgumentException>(() =>
            new AuditLog(DateTimeOffset.UtcNow, " ", AuditResult.Success, null, null, null, null, null, null));
    }

    [Fact]
    public void Resultado_com_falha_nao_expoe_valor()
    {
        Result<int> result = Error.NotFound("x", "não encontrado");

        Assert.False(result.IsSuccess);
        Assert.Throws<InvalidOperationException>(() => result.Value);
    }
}
