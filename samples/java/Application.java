// Display-only preview form of the demo stand: sign-in via the EXTERNAL Veriqa issuer on Java.
// The only mode is remote (a client of the external issuer); one file per language (the scenario
// does not fork the file — scenario specifics live in the cell instruction, not in code).
// A runnable version is a TODO.
//
// The client is a standard OIDC Relying Party built on Spring Security OAuth2 Client
// (spring-boot-starter-oauth2-client). No Veriqa-specific SDKs: Veriqa lives on the issuer side.
// Client registration (issuer-uri, client-id) is in application.yml (see the config tab).

package com.example.veriqa;

import java.security.Principal;

import org.springframework.boot.SpringApplication;
import org.springframework.boot.autoconfigure.SpringBootApplication;
import org.springframework.security.config.annotation.web.builders.HttpSecurity;
import org.springframework.security.web.SecurityFilterChain;
import org.springframework.context.annotation.Bean;
import org.springframework.web.bind.annotation.GetMapping;
import org.springframework.web.bind.annotation.RestController;

@SpringBootApplication
@RestController
public class Application {

    // region:snippet
    // Standard OIDC client to the external Veriqa issuer: enable OAuth2 login, every request
    // requires authentication. The provider (issuer-uri, client-id) is described in
    // application.yml — Spring Security performs Authorization Code + PKCE on its own.
    @Bean
    SecurityFilterChain security(HttpSecurity http) throws Exception {
        http
            .authorizeHttpRequests(auth -> auth.anyRequest().authenticated())
            .oauth2Login(login -> {});
        return http.build();
    }
    // endregion:snippet

    // region:snippet-routing
    // The login scenario: after signing in via a trusted channel, "Hello, {name}!" is shown.
    // The name comes from the Principal assembled from the external Veriqa issuer's claims.
    @GetMapping("/")
    public String home(Principal user) {
        return user != null ? "Hello, " + user.getName() + "!" : "Hello!";
    }

    public static void main(String[] args) {
        SpringApplication.run(Application.class, args);
    }
    // endregion:snippet-routing
}
