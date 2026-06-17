using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.JSInterop;

namespace JIroad.Security;

public class CustomAuthStateProvider : AuthenticationStateProvider
{
    private readonly IJSRuntime _jsRuntime;
    private readonly ClaimsPrincipal _anonymous = new(new ClaimsIdentity());

    public CustomAuthStateProvider(IJSRuntime jsRuntime)
    {
        _jsRuntime = jsRuntime;
    }

    public override async Task<AuthenticationState> GetAuthenticationStateAsync()
    {
        try
        {
            // Извлекаем токен из localStorage через твой JS-скрипт
            var token = await _jsRuntime.InvokeAsync<string>("jiroad.loadToken");
            if (string.IsNullOrWhiteSpace(token))
            {
                return new AuthenticationState(_anonymous);
            }

            var handler = new JwtSecurityTokenHandler();
            var jwtToken = handler.ReadJwtToken(token);
            
            // ОБНОВЛЕНО ДЛЯ POSTGRESQL/DOCKER: Принудительно приводим ValidTo к UTC перед сравнением временных меток
            var validToUtc = DateTime.SpecifyKind(jwtToken.ValidTo, DateTimeKind.Utc);
            if (validToUtc < DateTime.UtcNow)
            {
                return new AuthenticationState(_anonymous);
            }

            var identity = new ClaimsIdentity(jwtToken.Claims, "jwt", ClaimTypes.Name, ClaimTypes.Role);
            var user = new ClaimsPrincipal(identity);

            return new AuthenticationState(user);
        }
        catch
        {
            // Во время пререндеринга JS недоступен — возвращаем анонимного пользователя
            return new AuthenticationState(_anonymous);
        }
    }

    // Метод для вызова при логине/регистрации, чтобы мгновенно обновить интерфейс
    public void NotifyUserLogin(string token)
    {
        var handler = new JwtSecurityTokenHandler();
        var jwtToken = handler.ReadJwtToken(token);
        var identity = new ClaimsIdentity(jwtToken.Claims, "jwt", ClaimTypes.Name, ClaimTypes.Role);
        var user = new ClaimsPrincipal(identity);

        NotifyAuthenticationStateChanged(Task.FromResult(new AuthenticationState(user)));
    }

    // Метод для вызова при выходе из системы
    public void NotifyUserLogout()
    {
        NotifyAuthenticationStateChanged(Task.FromResult(new AuthenticationState(_anonymous)));
    }
}