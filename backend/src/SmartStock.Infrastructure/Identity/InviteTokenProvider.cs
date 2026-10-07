using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace SmartStock.Infrastructure.Identity;

/// <summary>
/// Token do link de primeiro acesso. Separado do token de recuperação para ter validade própria (mais longa).
/// Como o token inclui o security stamp, ele deixa de valer assim que a senha é definida.
/// </summary>
public sealed class InviteTokenProvider(
    IDataProtectionProvider dataProtectionProvider,
    IOptions<InviteTokenProviderOptions> options,
    ILogger<DataProtectorTokenProvider<AppUser>> logger)
    : DataProtectorTokenProvider<AppUser>(dataProtectionProvider, options, logger)
{
    public const string ProviderName = "Invite";
    public const string Purpose = "FirstAccess";
}

public sealed class InviteTokenProviderOptions : DataProtectionTokenProviderOptions
{
    public InviteTokenProviderOptions() => Name = InviteTokenProvider.ProviderName;
}
