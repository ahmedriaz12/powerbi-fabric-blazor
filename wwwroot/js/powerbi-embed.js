// Embeds Power BI content using powerbi-client (loaded from CDN in App.razor).
// Semantic: uses rendered event for timing. Paginated: heuristic only (no loaded/rendered).

(function () {
  /** TokenType.Embed === 1 in powerbi-client; some CDN builds omit powerbi.models. */
  function tokenTypeEmbed() {
    try {
      if (
        window.powerbi &&
        powerbi.models &&
        powerbi.models.TokenType !== undefined
      ) {
        return powerbi.models.TokenType.Embed;
      }
    } catch (e) {
      console.warn("powerbi.models.TokenType fallback", e);
    }
    return 1;
  }

  function reset(elementId) {
    var el = document.getElementById(elementId);
    if (!el) return;
    try {
      if (window.powerbi && typeof powerbi.reset === "function") {
        powerbi.reset(el);
      }
    } catch (e) {
      console.warn("powerbi.reset", e);
    }
    el.innerHTML = "";
  }

  function embedSemantic(elementId, embedUrl, accessToken, reportId) {
    return new Promise(function (resolve, reject) {
      var el = document.getElementById(elementId);
      if (!el) {
        reject("container not found");
        return;
      }
      reset(elementId);
      var tt = tokenTypeEmbed();
      var t0 = performance.now();
      var report = powerbi.embed(el, {
        type: "report",
        tokenType: tt,
        accessToken: accessToken,
        embedUrl: embedUrl,
        id: reportId,
      });
      report.on("error", function (ev) {
        reject(ev && ev.detail ? JSON.stringify(ev.detail) : "embed error");
      });
      report.on("rendered", function () {
        resolve({
          clientMs: Math.round(performance.now() - t0),
          signal: "rendered",
        });
      });
    });
  }

  function embedPaginated(elementId, embedUrl, accessToken, reportId) {
    return new Promise(function (resolve, reject) {
      var el = document.getElementById(elementId);
      if (!el) {
        reject("container not found");
        return;
      }
      reset(elementId);
      var tt = tokenTypeEmbed();
      var t0 = performance.now();
      var report = powerbi.embed(el, {
        type: "report",
        tokenType: tt,
        accessToken: accessToken,
        embedUrl: embedUrl,
        id: reportId,
        settings: {
          commands: {
            parameterPanel: {
              enabled: true,
              expanded: true,
            },
          },
        },
      });
      report.on("error", function (ev) {
        reject(ev && ev.detail ? JSON.stringify(ev.detail) : "embed error");
      });
      setTimeout(function () {
        resolve({
          clientMs: Math.round(performance.now() - t0),
          signal: "heuristic_no_rendered_event",
        });
      }, 1000);
    });
  }

  window.pbiEmbed = {
    reset: reset,
    embedSemantic: embedSemantic,
    embedPaginated: embedPaginated,
  };
})();
