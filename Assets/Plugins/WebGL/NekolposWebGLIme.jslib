mergeInto(LibraryManager.library, {
  NekolposWebGLIme_Register: function (objectNamePtr, debugLogging) {
    var state = window.NekolposWebGLIme || {};
    var hasDebugFlag = function (value) {
      return /(?:\?|&)nekolpos_ime_debug=1(?:&|$)/.test(value || "");
    };
    var urlDebug = false;

    try {
      urlDebug = hasDebugFlag(window.location.href) ||
        hasDebugFlag(document.referrer) ||
        window.localStorage.getItem("nekolpos_ime_debug") === "1";
    } catch (error) {
    }

    try {
      if (!urlDebug && window.parent && window.parent !== window) {
        urlDebug = hasDebugFlag(window.parent.location.href) ||
          window.parent.localStorage.getItem("nekolpos_ime_debug") === "1";
      }
    } catch (error) {
    }

    state.registeredObjectName = UTF8ToString(objectNamePtr);
    state.debug = debugLogging !== 0 || urlDebug;
    window.NekolposWebGLIme = state;

    if (state.debug && typeof console !== "undefined" && console.log) {
      console.log("[NekolposWebGLIme] register", {
        objectName: state.registeredObjectName,
        debugBuild: debugLogging !== 0,
        urlDebug: urlDebug
      });
    }
  },

  NekolposWebGLIme_Show: function (objectNamePtr, textPtr, x, y, width, height, fontSize, colorPtr) {
    var state = window.NekolposWebGLIme || {};
    window.NekolposWebGLIme = state;

    var nextObjectName = UTF8ToString(objectNamePtr);
    var ownerChanged = state.objectName !== nextObjectName;
    state.objectName = nextObjectName;

    var debugLog = function () {
      if (!state.debug || typeof console === "undefined" || !console.log) {
        return;
      }

      console.log.apply(console, arguments);
    };

    var input = state.input;
    if (!input) {
      input = document.createElement("input");
      input.type = "text";
      input.disabled = false;
      input.readOnly = false;
      input.autocomplete = "off";
      input.autocapitalize = "off";
      input.spellcheck = false;
      input.inputMode = "text";
      input.lang = "ja";
      input.tabIndex = 0;
      input.style.position = "fixed";
      input.style.zIndex = "2147483647";
      input.style.boxSizing = "border-box";
      input.style.border = "0";
      input.style.outline = "0";
      input.style.padding = "0 10px";
      input.style.margin = "0";
      input.style.background = "transparent";
      input.style.boxShadow = "none";
      input.style.borderRadius = "0";
      input.style.textDecoration = "none";
      input.style.pointerEvents = "none";
      input.style.fontFamily = "sans-serif";
      input.style.lineHeight = "1.2";
      input.style.visibility = "visible";
      input.style.webkitAppearance = "none";
      input.style.appearance = "none";

      var sendUnity = function (method, value) {
        if (!state.objectName) {
          return;
        }

        if (typeof SendMessage === "function") {
          SendMessage(state.objectName, method, value);
        } else if (typeof Module !== "undefined" && Module && typeof Module.SendMessage === "function") {
          Module.SendMessage(state.objectName, method, value);
        }
      };

      var sendText = function () {
        sendUnity("OnWebGLImeTextChanged", input.value);
      };

      var focusCanvas = function () {
        if (typeof Module !== "undefined" && Module && Module.canvas && typeof Module.canvas.focus === "function") {
          Module.canvas.tabIndex = Module.canvas.tabIndex >= 0 ? Module.canvas.tabIndex : 0;
          Module.canvas.focus({ preventScroll: true });
        }
      };

      var isEnterKey = function (event) {
        return event.key === "Enter" ||
          event.code === "Enter" ||
          event.keyCode === 13 ||
          event.which === 13;
      };

      var isEscapeKey = function (event) {
        return event.key === "Escape" ||
          event.code === "Escape" ||
          event.key === "Esc" ||
          event.keyCode === 27 ||
          event.which === 27;
      };

      var isHistoryKey = function (event) {
        return event.key === "ArrowUp" ||
          event.code === "ArrowUp" ||
          event.keyCode === 38 ||
          event.which === 38 ||
          event.key === "ArrowDown" ||
          event.code === "ArrowDown" ||
          event.keyCode === 40 ||
          event.which === 40;
      };

      var sendHistoryNavigation = function (event) {
        var isPrevious = event.key === "ArrowUp" ||
          event.code === "ArrowUp" ||
          event.keyCode === 38 ||
          event.which === 38;
        sendUnity(isPrevious ? "OnWebGLImeHistoryPrevious" : "OnWebGLImeHistoryNext", input.value);
      };

      var submitFromKeyboard = function (event) {
        event.stopPropagation();
        debugLog("[NekolposWebGLIme] " + event.type, {
          key: event.key,
          code: event.code,
          keyCode: event.keyCode,
          which: event.which,
          isComposing: event.isComposing
        });

        if (isHistoryKey(event) && !event.isComposing && !state.isComposing) {
          event.preventDefault();
          if (event.type === "keydown") {
            sendHistoryNavigation(event);
          }
          return;
        }

        if (isEscapeKey(event) && !event.isComposing && !state.isComposing) {
          event.preventDefault();
          if (event.type === "keydown") {
            sendUnity("OnWebGLImeEscape", input.value);
          }
          return;
        }

        if (!isEnterKey(event) || event.isComposing || state.isComposing) {
          return;
        }

        event.preventDefault();
        var eventTime = event.timeStamp || Date.now();
        if (state.lastSubmitEventTime && Math.abs(eventTime - state.lastSubmitEventTime) < 10) {
          return;
        }

        state.lastSubmitEventTime = eventTime;
        sendUnity("OnWebGLImeSubmit", input.value);
      };

      input.addEventListener("focus", function () {
        debugLog("[NekolposWebGLIme] focus", {
          activeElement: document.activeElement,
          inputIsActiveElement: input === document.activeElement,
          disabled: input.disabled,
          readOnly: input.readOnly,
          display: input.style.display,
          visibility: input.style.visibility
        });
        sendUnity("OnWebGLImeFocus", input.value);
      }, true);

      input.addEventListener("beforeinput", function (event) {
        debugLog("[NekolposWebGLIme] beforeinput", {
          inputType: event.inputType,
          data: event.data,
          isComposing: event.isComposing
        });
      }, true);

      input.addEventListener("input", function (event) {
        event.stopPropagation();
        debugLog("[NekolposWebGLIme] input", {
          value: input.value,
          isComposing: event.isComposing
        });
        if (event.isComposing || state.isComposing) {
          return;
        }

        sendText();
      }, true);

      input.addEventListener("compositionstart", function (event) {
        event.stopPropagation();
        state.isComposing = true;
        debugLog("[NekolposWebGLIme] compositionstart", {
          data: event.data
        });
        sendUnity("OnWebGLImeCompositionStart", input.value);
      }, true);

      input.addEventListener("compositionupdate", function (event) {
        event.stopPropagation();
        debugLog("[NekolposWebGLIme] compositionupdate", {
          data: event.data
        });
      }, true);

      input.addEventListener("compositionend", function (event) {
        event.stopPropagation();
        state.isComposing = false;
        debugLog("[NekolposWebGLIme] compositionend", {
          data: event.data
        });
        sendUnity("OnWebGLImeCompositionEnd", input.value);
      }, true);

      input.addEventListener("keydown", submitFromKeyboard, true);
      input.addEventListener("keypress", submitFromKeyboard, true);

      input.addEventListener("keyup", function (event) {
        event.stopPropagation();
      }, true);

      input.addEventListener("blur", function () {
        debugLog("[NekolposWebGLIme] blur", {
          activeElement: document.activeElement,
          inputIsActiveElement: input === document.activeElement,
          disabled: input.disabled,
          readOnly: input.readOnly,
          display: input.style.display,
          visibility: input.style.visibility
        });
        sendUnity("OnWebGLImeBlur", input.value);
      }, true);

      document.addEventListener("pointerdown", function (event) {
        focusCanvas();

        if (!state.input || state.input.style.display === "none") {
          return;
        }

        var rect = state.input.getBoundingClientRect();
        var x = event.clientX;
        var y = event.clientY;
        var insideInput =
          x >= rect.left &&
          x <= rect.right &&
          y >= rect.top &&
          y <= rect.bottom;

        if (insideInput) {
          return;
        }

        debugLog("[NekolposWebGLIme] external pointerdown", {
          x: x,
          y: y,
          inputLeft: rect.left,
          inputTop: rect.top,
          inputRight: rect.right,
          inputBottom: rect.bottom
        });
        sendUnity("OnWebGLImeExternalPointerDown", state.input.value);
      }, true);

      document.body.appendChild(input);
      state.input = input;
    }

    var canvas = Module.canvas;
    var canvasRect = canvas.getBoundingClientRect();
    var scaleX = canvasRect.width / Math.max(1, canvas.width);
    var scaleY = canvasRect.height / Math.max(1, canvas.height);
    var text = UTF8ToString(textPtr);
    var color = UTF8ToString(colorPtr);

    if (!color || /^#?[0-9a-fA-F]{6}00$/.test(color)) {
      color = "#ffffff";
    }

    input.type = "text";
    input.disabled = false;
    input.readOnly = false;
    input.inputMode = "text";
    input.style.visibility = "visible";
    input.style.left = (canvasRect.left + x * scaleX) + "px";
    input.style.top = (canvasRect.top + (canvas.height - y - height) * scaleY) + "px";
    input.style.width = Math.max(1, width * scaleX) + "px";
    input.style.height = Math.max(1, height * scaleY) + "px";
    input.style.fontSize = Math.max(12, fontSize * scaleY) + "px";
    input.style.opacity = "1";
    input.style.color = color;
    input.style.textShadow = "none";
    input.style.textDecoration = "none";
    input.style.caretColor = color;
    input.style.display = "block";
    input.style.pointerEvents = "none";

    if (ownerChanged || document.activeElement !== input) {
      input.value = text;
      input.focus({ preventScroll: true });
      input.setSelectionRange(input.value.length, input.value.length);
    }

    debugLog("[NekolposWebGLIme] show", {
      activeElement: document.activeElement,
      inputIsActiveElement: input === document.activeElement,
      disabled: input.disabled,
      readOnly: input.readOnly,
      display: input.style.display,
      visibility: input.style.visibility
    });
  },

  NekolposWebGLIme_Deactivate: function (objectNamePtr) {
    var state = window.NekolposWebGLIme;
    if (!state || !state.input) {
      return;
    }

    var objectName = UTF8ToString(objectNamePtr);
    if (state.objectName && objectName && state.objectName !== objectName) {
      return;
    }

    state.input.style.display = "none";
    if (document.activeElement === state.input) {
      state.input.blur();
    }
    if (typeof Module !== "undefined" && Module && Module.canvas && typeof Module.canvas.focus === "function") {
      Module.canvas.tabIndex = Module.canvas.tabIndex >= 0 ? Module.canvas.tabIndex : 0;
      Module.canvas.focus({ preventScroll: true });
    }
  },

  NekolposWebGLIme_Hide: function (objectNamePtr) {
    var state = window.NekolposWebGLIme;
    if (!state || !state.input) {
      return;
    }

    var objectName = UTF8ToString(objectNamePtr);
    if (state.objectName && objectName && state.objectName !== objectName) {
      return;
    }

    state.input.style.display = "none";
    if (document.activeElement === state.input) {
      state.input.blur();
    }
    if (typeof Module !== "undefined" && Module && Module.canvas && typeof Module.canvas.focus === "function") {
      Module.canvas.tabIndex = Module.canvas.tabIndex >= 0 ? Module.canvas.tabIndex : 0;
      Module.canvas.focus({ preventScroll: true });
    }
  },

  NekolposWebGLIme_HideAll: function () {
    var state = window.NekolposWebGLIme;
    if (!state || !state.input) {
      return;
    }

    state.input.style.display = "none";
    state.objectName = null;
    if (document.activeElement === state.input) {
      state.input.blur();
    }
    if (typeof Module !== "undefined" && Module && Module.canvas && typeof Module.canvas.focus === "function") {
      Module.canvas.tabIndex = Module.canvas.tabIndex >= 0 ? Module.canvas.tabIndex : 0;
      Module.canvas.focus({ preventScroll: true });
    }
  },

  NekolposWebGLIme_SetText: function (textPtr) {
    var state = window.NekolposWebGLIme;
    if (!state || !state.input) {
      return;
    }

    var text = UTF8ToString(textPtr);
    if (state.input.value !== text) {
      state.input.value = text;
      state.input.setSelectionRange(text.length, text.length);
    }
  }
});
