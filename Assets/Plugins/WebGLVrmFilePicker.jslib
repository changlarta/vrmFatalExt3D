mergeInto(LibraryManager.library, {
  WebGLVrmPicker_Open: function (gameObjectNamePtr, callbackMethodNamePtr) {
    var goName = UTF8ToString(gameObjectNamePtr);
    var cbName = UTF8ToString(callbackMethodNamePtr);

    if (!Module.__vrmPickerInput) {
      var input = document.createElement("input");
      input.type = "file";
      input.accept = ".vrm";
      input.multiple = false;
      input.style.display = "none";
      document.body.appendChild(input);
      Module.__vrmPickerInput = input;
    }

    var inp = Module.__vrmPickerInput;

    inp.onchange = function () {
      try {
        var file = inp.files && inp.files.length > 0 ? inp.files[0] : null;
        if (!file) {
          Module.__vrmPickerBytes = null;
          SendMessage(goName, cbName, "0");
          return;
        }

        // ★拡張子チェック（acceptは信用しない）
        var name = file.name || "";
        if (!/\.vrm$/i.test(name)) {
          Module.__vrmPickerBytes = null;
          SendMessage(goName, cbName, "0");
          return;
        }

        var reader = new FileReader();
        reader.onload = function () {
          var arrayBuffer = reader.result;
          if (!arrayBuffer) {
            Module.__vrmPickerBytes = null;
            SendMessage(goName, cbName, "0");
            return;
          }
          var bytes = new Uint8Array(arrayBuffer);
          Module.__vrmPickerBytes = bytes;
          SendMessage(goName, cbName, bytes.length.toString());
        };
        reader.onerror = function () {
          Module.__vrmPickerBytes = null;
          SendMessage(goName, cbName, "0");
        };

        reader.readAsArrayBuffer(file);
      } catch (err) {
        Module.__vrmPickerBytes = null;
        SendMessage(goName, cbName, "0");
      } finally {
        // allow selecting the same file again
        inp.value = "";
      }
    };

    // Must be triggered by a user gesture
    inp.click();
  },

  WebGLVrmPicker_CopyTo: function (dstPtr) {
    var bytes = Module.__vrmPickerBytes;
    if (!bytes || bytes.length === 0) return;
    HEAPU8.set(bytes, dstPtr);
  },

  WebGLVrmPicker_Clear: function () {
    Module.__vrmPickerBytes = null;
  },
});
