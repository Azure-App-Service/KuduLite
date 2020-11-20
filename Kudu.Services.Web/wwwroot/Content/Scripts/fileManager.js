//Calling dropzone JavaScript

//Dropzone 1 for Drag/Drop files & Folders
Dropzone.autoDiscover = false;
var myDropzone1 = new Dropzone(
    '#upload-widget', {
    addRemoveLinks: true,
    disablePreview: true,
    dictDefaultMessage: 'Drag a File/Folder here to upload, or click to select one',
    accept: function (file, done) {
        $('.dz-preview.dz-file-preview').css("display", "none"); //removing preview element of dropzone
        showhidepaKmanLoader(true);
        progressBarDisplay(true);
        // Create a FormData object.
        var formData = new FormData();
        formData.append('file', file);
        if (file.fullPath == undefined) {
            var url = '/api/vfs' + curPath + file.name;
        }
        else {
            var url = '/api/vfs' + curPath + file.fullPath;
        }

        //New XMLHttpRequest object
        let request = new XMLHttpRequest();
        request.open('PUT', url);
        request.setRequestHeader("Content-Type", "application/x-www-form-urlencoded; charset=UTF-8");
        request.setRequestHeader("If-Match", "*");

        // upload progress event
        request.upload.addEventListener('progress', function (e) {
            let perc = (e.loaded / e.total) * 100;
            $('#copy-percentage').text(perc + "%");
        });

        // Response
        request.addEventListener('load', function (e) {
            // HTTP status message
            showhidepaKmanLoader(false);
            progressBarDisplay(false);
            $(".dz-default.dz-message").css("display", "block");
            $.get(("/api/vfs" + curPath.trim()), null, function (data) {
                generateDynamicTable(data);
            });
        });

        // On error
        request.onerror = function () {
            showhidepaKmanLoader(false);
            progressBarDisplay(false);
            alert("Error while PUT request!");
        };

        // Submit Request with zip file
        request.send(file);
    }
});

//Dropzone 2 for drag/drop zip files to extract the zip files in the respective directory
var myDropzone2 = new Dropzone(
    '#upload-widget-zip', {
    addRemoveLinks: true,
    disablePreview: true,
    dictDefaultMessage: 'Drag a Zip File here to extract & upload, or click to select one',
    accept: function (file, done) {
        debugger;
        $('.dz-preview.dz-file-preview').css("display", "none"); //removing preview element of dropzone
        showhidepaKmanLoader(true);
        progressBarDisplay(true);
        // Create a FormData object.
        var formData = new FormData();
        formData.append('file', file);
        if (file.name.indexOf(".zip") > 0) {
            if (file.fullPath == undefined) {
                var url = '/api/zip' + curPath + file.name;
                //url = url.replace(".zip", "");
                url = url.replace(file.name, "");
            }
            else {
                var url = '/api/zip' + curPath + file.fullPath;
                url = url.replace(".zip", "");
            }
        }
        else {
            if (file.fullPath == undefined) {
                var url = '/api/vfs' + curPath + file.name;
            }
            else {
                var url = '/api/vfs' + curPath + file.fullPath;
            }
        }

        //New XMLHttpRequest object
        let request = new XMLHttpRequest();
        request.open('PUT', url);
        request.setRequestHeader("Content-Type", "application/x-www-form-urlencoded; charset=UTF-8");
        request.setRequestHeader("If-Match", "*");

        // upload progress event
        request.upload.addEventListener('progress', function (e) {
            let perc = (e.loaded / e.total) * 100;
            $('#copy-percentage').text(perc + "%");
        });

        // Response
        request.addEventListener('load', function (e) {
            // HTTP status message
            if (request.status != 200) { // analyze HTTP status of the response
                alert(`Error ${request.status}: ${request.statusText}`); // e.g. 404: Not Found
            } else { // show the result
                showhidepaKmanLoader(false);
                progressBarDisplay(false);
                $(".dz-default.dz-message").css("display", "block");
                $.get(("/api/vfs" + curPath.trim()), null, function (data) {
                    generateDynamicTable(data);
                });
            }
        });

        // On error
        request.onerror = function () {
            showhidepaKmanLoader(false);
            progressBarDisplay(false);
            alert("Error while PUT request!");
        };

        // Submit Request with zip file
        request.send(file);
    }
});

//File Manager - Main Section
var root = "/";
var curPath = "/";

$.get("/api/vfs", null, function (data) {
    generateDynamicTable(data);
});

function generateDynamicTable(fileContent) {
    var dataFileManager = fileContent.length + 1;

    if (dataFileManager > 0) {

        //generation of dynamic table, based on data.
        var table = document.createElement("table");
        table.id = "fileManagerTable";
        table.className = "table table-striped table-bordered table-hover table-condensed table-responsive";
        table.style.width = '100%';
        table.style.display = 'table';

        // retrieve column header
        var col = []; // define an empty array
        for (var i = 0; i < dataFileManager; i++) {
            for (var key in fileContent[i]) {
                if (col.indexOf(key) === -1) {
                    col.push(key);
                }
            }
        }

        // CREATE TABLE HEAD .
        var tHead = document.createElement("thead");


        //Add table header row
        var hRow = document.createElement("tr");

        //Add an empty default column for the table
        var emptyHeader = document.createElement("th");
        emptyHeader.style.width = "10%";
        hRow.appendChild(emptyHeader);

        //Adding column headers (with header names) to the table.
        for (var i = 0; i < col.length; i++) {
            var th = document.createElement("th");
            if ((col[i] != "name") && (col[i] != "size") && (col[i] != "mtime")) {
                th.innerHTML = col[i];
                th.style.display = "none";
            }
            if (col[i] == "name") {
                th.style.width = "50%";
                th.innerText = "Name";
            }
            if (col[i] == "size") {
                th.style.width = "20%";
                th.innerText = "Size";
            }
            if (col[i] == "mtime") {
                th.style.width = "20%";
                th.innerText = "Modified Time";
            }
            th.style.padding = "0px";
            hRow.appendChild(th);
        }
        tHead.appendChild(hRow);
        table.appendChild(tHead);

        //creating table body.
        var tBody = document.createElement("tbody");

        //Adding column to the rows for the respective table.
        for (var i = 0; i < dataFileManager; i++) {

            var bRow = document.createElement("tr"); //Create a row for each record.

            for (var j = -1; j < col.length; j++) {
                if (fileContent[i] != undefined) { //checking the default row added for blank folders - skip assignment if data does not exist.
                    var td = document.createElement("td");
                    if (j == -1) {
                        if ((fileContent[i][col[4]]).indexOf("directory") > 0) {
                            td.innerHTML = "<i class='fa fa-times' style='cursor:pointer;' id='delIcon' title='delete' aria-hidden='true'></i>&nbsp;&nbsp;" +
                                "<a id='dwnIcon' href='#'><i class='fa fa-download' aria-hidden='true'></i></a>";
                        }
                        else {
                            td.innerHTML = "<i class='fa fa-times' style='cursor:pointer;' id='delIcon' title='delete' aria-hidden='true'></i>&nbsp;&nbsp;" +
                                "<i class='fas fa-pencil-alt' style='cursor:pointer;' id='editIcon' title='edit' aria-hidden='true'></i>&nbsp;&nbsp;" +
                                "<a id='dwnIcon' href='#'><i class='fa fa-download' aria-hidden='true'></i></a>";
                        }
                    }
                    else if (j == 0) {
                        //check to set the name of the file/folder
                        if ((fileContent[i][col[4]]).indexOf("directory") > 0) {
                            td.innerHTML = "<span><i class='fa fa-folder' aria-hidden='true'></i></span>&nbsp;" +
                                "<a name='fname' href='#'>" + fileContent[i][col[j]] + "</a>";
                        }
                        else {
                            td.innerHTML = "<span><i class='fa fa-file' aria-hidden='true'></i></span>&nbsp;" +
                                "<a name='fname' href='#'>" + fileContent[i][col[j]] + "</a>";
                        }
                    }
                    else if (j == 1) {
                        //check to set the size of the file/folder
                        if ((fileContent[i][col[4]]).indexOf("directory") <= 0) {
                            td.innerHTML = "<span name='fsize'>" + fileContent[i][col[j]] ? (Math.ceil(fileContent[i][col[j]] / 1024) + ' KB') : '' + "</span>";
                        }
                    }
                    else if (j == 2) {
                        //check to set the modified time of the file/folder
                        td.innerHTML = "<span name='fmdtime'>" + ((fileContent[i][col[j]] && new Date(fileContent[i][col[j]])) || new Date()).toLocaleString() + "</span>";
                    }
                    else {
                        td.innerHTML = fileContent[i][col[j]];
                        td.style.display = "none";
                    }
                    bRow.appendChild(td);
                }
            }
            tBody.appendChild(bRow)
        }
        table.appendChild(tBody);


        //add the dynamically created table to the div - divFileManager.
        var divContainer = document.getElementById("divFileManager");
        if (divContainer != null) {
            if (divContainer.innerHTML.length > 0) {
                divContainer.innerHTML = "";
            }
            divContainer.appendChild(table);
            reStructure();
        }

    }
}

//function to put file manager div after dropzone div
function reStructure() {
    $("#divFileManager").insertAfter(".dz-default.dz-message:first");
}

//Tracking click event on file/folder name - would be used for in page edit of files.
$(document).on("click", "a[name='fname']", function (e) {
    if ((e.currentTarget.parentElement.parentElement.cells[5].innerHTML).indexOf("directory") > 0) {
        curPath = curPath + e.currentTarget.parentElement.parentElement.cells[1].innerText.trim() + "/";
        $("#Path").val(curPath);
        $.get(e.currentTarget.parentElement.parentElement.cells[6].innerHTML, null, function (data) {
            generateDynamicTable(data);
            //$("#spaddPath").text(root);
            if ($("#spaddPath").text() == "/") {
                $("#spaddPath").text(curPath);
            }
            else {
                $("#spaddPath").text() == "/"
                $("#spaddPath").text(curPath);
            }
        });
    }
    else {
        $.get(e.currentTarget.parentElement.parentElement.cells[6].innerHTML, null, function (data) {
            e.currentTarget.href = e.currentTarget.parentElement.parentElement.cells[6].innerHTML;
            window.open(
                e.currentTarget.href,
                '_blank'
            );
        });
    }
});

//Click event of anchor tag - aCurPath; sets the path to previous directory.
$(document).on("click", "a[id='aCurPath']", function (e) {
    if ($("#spaddPath").text() != '') {
        curPath = curPath.split("/");
        curPath.pop();
        curPath.pop();
        curPath = curPath.join("/").trim() + "/";
        $.get(("/api/vfs" + curPath), null, function (data) {
            generateDynamicTable(data);
            $("#spaddPath").text(curPath);
        });
    }
});

//Tracking click event of delete Icon
$(document).on("click", "i[id='delIcon']", function (e) {
    var result = confirm('Are you sure to delete the file?');
    if (result == true) {
        showhidepaKmanLoader(true);
        var url = e.currentTarget.parentElement.parentElement.cells[6].innerHTML;

        if (e.currentTarget.parentElement.parentElement.cells[5].innerHTML === "inode/directory") {
            url += "?recursive=true";
        }
        $.ajax({
            url: url,
            method: "DELETE",
            headers: {
                "If-Match": "*"
            },
            success: function (result) {
                showhidepaKmanLoader(false);
                $.get(("/api/vfs" + curPath.trim()), null, function (data) {
                    generateDynamicTable(data);
                });
            },
            error: function (error) {
                showhidepaKmanLoader(false);
                alert("Error in delete request!");
            }
        });
    }
    else {
        return;
    }
});

//Tracking click event of edit Icon
$(document).on("click", "i[id='editIcon']", function (e) {
    $(".edit-view").css('display', 'block');
    //$("#aCurPath").css('display', 'none');
    $("#containerFileManager").css('display', 'none');

    if ($('.edit-view').is(':visible')) {
        debugger;
        editor.setValue('');
        var url = e.currentTarget.parentElement.parentElement.cells[6].innerHTML;
        var filename = e.currentTarget.parentElement.parentElement.cells[1].innerText.trim();
        var mimeType = e.currentTarget.parentElement.parentElement.cells[5].innerText.trim()
        //alert('clicked');
        statusbar.fetchingContents();
        getContent(url).then(function (result) {
            editor.setValue(result);
            if (mimeType === 'text/xml') {
                result = vkbeautify.xml(result);
            }
            statusbar.showFilename(filename);
            if (typeof filename !== 'undefined') {
                var modelist = ace.require('ace/ext/modelist');
                var mode = modelist.getModeForPath(filename).mode;
                if (mode === 'ace/mode/text') {
                    mode = getCustomMode(filename);
                }
                // Apply computed syntax mode or default to 'ace/mode/text'
                editor.session.setMode(mode);
            }
            // Set Ace height
            resizeAce();
            editor.focus();
            // Attach event handler to set new Ace height on browser resize
            $(window).on('resize', function () {
                resizeAce();
            });
        });
    }
});

//Tracking click event of download Icon
$(document).on("click", "a[id='dwnIcon']", function (e) {
    showhidepaKmanLoader(true);
    var element = document.createElement('a');
    if ((e.currentTarget.parentElement.parentElement.cells[5].innerHTML).indexOf("directory") > 0) {
        var zipUrl = (e.currentTarget.parentElement.parentElement.cells[6].innerText).replace("/vfs/", "/zip/")
        element.setAttribute('href', zipUrl);
        element.setAttribute('download', e.currentTarget.parentElement.parentElement.cells[1].innerText.trim() + ".zip");
    }
    else {
        element.setAttribute('href', e.currentTarget.parentElement.parentElement.cells[6].innerText);
        element.setAttribute('download', e.currentTarget.parentElement.parentElement.cells[1].innerText.trim());
    }
    element.style.display = 'none';
    document.body.appendChild(element);
    element.click();
    document.body.removeChild(element);
    showhidepaKmanLoader(false);
});

// **********Ace Combined script from ace - init.js and filebrowser.js************
// Resize editor window based on browser window.innerHeight
function resizeAce() {
    // http://stackoverflow.com/questions/11584061/
    var new_height = (window.innerHeight - 170) + 'px';
    $('#editor').css({ 'height': new_height });
    editor.resize();
}

// Additional syntax highlight logic
function getCustomMode(filename) {
    var _config = (/^(web|app).config$/i);
    var _csproj = (/.(cs|vb)proj$/i);
    var _xdt = (/.xdt$/i);
    var _aspnet = (/.(cshtml|asp|aspx)$/i);
    var _csx = (/.csx$/i);
    var syntax_mode = 'ace/mode/text';
    if (filename.match(_config) ||
        filename.match(_csproj) ||
        filename.match(_xdt)) {
        syntax_mode = 'ace/mode/xml';
    }
    if (filename.match(_aspnet) ||
        filename.match(_csx)) {
        syntax_mode = 'ace/mode/csharp';
    }

    return syntax_mode;
}

// Act II - The Editor Awakens
var editor = ace.edit("editor");
editor.setTheme("ace/theme/github");
editor.getSession().setTabSize(4);
editor.getSession().setUseSoftTabs(true);
editor.$blockScrolling = Infinity;
editor.setOptions({
    "showPrintMargin": false,
    "fontSize": 14
});

// Show a red bar if content has changed
var contentHasChanged = false;
editor.on('change', function () {
    // (Attempt to) separate user change from programatical
    // https://github.com/ajaxorg/ace/issues/503
    if (editor.curOp && editor.curOp.command.name) {
        if (contentHasChanged) {
            return;
        }
        $('#statusbar').removeClass('statusbar-saved');
        $('#statusbar').addClass('statusbar-red');
        // Let's be nice to jQuery and only .addClass() on first change
        contentHasChanged = true;
    }
});

// Bind CTRL-S as Save without closing
editor.commands.addCommand({
    name: 'saveItem',
    bindKey: {
        win: 'Ctrl-S',
        mac: 'Command-S',
        sender: 'editor|cli'
    },
    exec: function (env, args, request) {
        saveItem();
    }
});

this.saveItem = function () {
    debugger;
    var text = editor.getValue();
    statusbar.savingChanges();
    this.setContent(this, text)
        .done(function () {
            statusbar.acknowledgeSave();
        }).fail(function (error) {
            removeAllToasts();
            showErrorAsToast(error);
            statusbar.errorState.set();
        });
}

this.saveItemAndClose = function () {
    var text = editor.getValue();
    statusbar.savingChanges();
    this.setContent(this, text)
        .done(function () {
            statusbar.reset();

            $(".edit-view").css('display', 'none');
            $("#containerFileManager").css('display', 'block');
            //removeAllToasts();
            $.get(("/api/vfs" + curPath.trim()), null, function (data) {
                generateDynamicTable(data);
            });
        }).fail(function (error) {
            removeAllToasts();
            showErrorAsToast(error);
            statusbar.errorState.set();
        });
}

this.setContent = function (item, text) {
    var _url = '/api/vfs' + curPath + item.filename.trim();
    return $.ajax({
        url: _url,
        data: text,
        method: "PUT",
        xhr: function () {  // Custom XMLHttpRequest
            var myXhr = $.ajaxSettings.xhr();
            if (myXhr.upload) { // Check if upload property exists
                myXhr.upload.addEventListener('progress', function (e) {
                    copyProgressHandlingFunction(e, _url);
                }, false); // For handling the progress of the upload
            }
            return myXhr;
        },
        processData: false,
        headers: {
            "If-Match": "*"
        }
    });
}

this.getContent = function (item) {
    return $.ajax({
        url: item,
        dataType: "text",
        headers: {
            "If-Match": "*"
        }
    });
}

this.cancelEdit = function () {
    editor.setValue('');
    statusbar.reset();
    $(".edit-view").css('display', 'none');
    $("#containerFileManager").css('display', 'block');
    removeAllToasts();
    $.get(("/api/vfs" + curPath.trim()), null, function (data) {
        generateDynamicTable(data);
    });
}
//monitor file upload progress
function copyProgressHandlingFunction(e, uniqueUrl, forceUpdateModal) {
    if (e && uniqueUrl && e.lengthComputable) {
        copyObjectsManager.addCopyStats(uniqueUrl, e.loaded, e.total); //add/update stats
    }
    var perc = copyObjectsManager.getCurrentPercentCompletion(); // perc-per-total transaction
    var copyObjs = copyObjectsManager.getCopyStats();

    $('#copy-percentage').text(perc + "%");

    if (perc != 100 && perc != 0) {
        viewModel.isTransferInProgress(true);
    }

    //handler for clearing out cache once it gets too large
    var currentObjCount = Object.keys(copyObjs).length;
    if (currentObjCount > 2000) {
        for (var i = 0; i < 1000; i++) { //delete oldest 1000 copy prog objects
            copyObjectsManager.removeAtIndex(0);
        }
        var date = new Date();
        copyObjectsManager.setInfoMessage('Cache was partialy auto-cleared at ' + date.toLocaleString() + ' for performance improvements');
    }

    if ($('#files-transfered-modal').is(':visible') || forceUpdateModal) { // update if modal visible
        viewModel.copyProgStats(copyObjs); // update viewmodel

        var modalHeaderText = '';
        if (perc < 100) {
            modalHeaderText = 'Transferred Files (<b>' + perc + '%</b>).';
        } else {
            modalHeaderText = '<b style =\' color:green\'> Transferred Files (' + perc + '%).</b>';
        }
        modalHeaderText += ' ' + ((_temp = copyObjectsManager.getInfoMessage()) ? _temp : "");
        $('#files-transfered-modal .modal-header').html(modalHeaderText);
    }

}

// Hook the little pencil glyph and apply Ace syntax mode based on file extension
//$('.edit').on('click', '.fa-pencil', function () {
$("#editIcon").click(function () {
    debugger;
    if ($('.edit-view').is(':visible')) {
        var filename;
        try {
            filename = (window.viewModel.fileEdit.peek()).name();
        }
        catch (e) {
            if (typeof console == 'object') {
                console.log('Can not get filename. ' + e);
            }
        }
        finally {
            if (typeof filename !== 'undefined') {
                var modelist = ace.require('ace/ext/modelist');
                var mode = modelist.getModeForPath(filename).mode;
                if (mode === 'ace/mode/text') {
                    mode = getCustomMode(filename);
                }
                // Apply computed syntax mode or default to 'ace/mode/text'
                editor.session.setMode(mode);
            }
            // Set Ace height
            resizeAce();
            editor.focus();
            // Attach event handler to set new Ace height on browser resize
            $(window).on('resize', function () {
                resizeAce();
            });
        }
    }
});

// Custom status bar for Ace (aka Project Wunderbar)
var statusbar = {
    showFilename:
        function (fileName) {
            try {
                if (fileName == undefined) {
                    filename = filename;
                }
                else {
                    filename = fileName
                }
            }
            catch (e) {
                filename = 'Can not get filename. See console for details.';
                if (typeof console == 'object') {
                    console.error('Can not get filename: %s', e);
                }
            }
            finally {
                $('#statusbar').text(filename);
            }
        },
    reset:
        function () {
            $('#statusbar').text('');
            $('#statusbar').removeClass('statusbar-red');
            $('#statusbar').removeClass('statusbar-saved');
            $('#statusbar').css('background', 'none');
            // Clear editor window
            editor.setValue('');
            // Flag from ace-init.js
            contentHasChanged = false;
            // Clear search box
            if (editor.searchBox) {
                editor.searchBox.activeInput.value = '';
                editor.searchBox.hide();
            }
        },
    savingChanges:
        function () {
            $('#statusbar').text('Saving changes...');
            $('#statusbar').prepend('<i class="glyphicon glyphicon-cloud-upload" style="margin-right: 6px"></i>');
        },
    fetchingContents:
        function () {
            $('#statusbar').text('Fetching contents...');
            $('#statusbar').prepend('<i class="glyphicon glyphicon-cloud-download" style="margin-right: 6px"></i>');
        },
    acknowledgeSave:
        function () {
            this.errorState.remove();
            $('#statusbar').addClass('statusbar-saved');
            contentHasChanged = false;
            this.showFilename();
        },
    errorState:
    {
        set: function () {
            // We could not save the file
            // Mild panic attack, turn statusbar red
            statusbar.showFilename();
            $('#statusbar').css('background', '#ffdddd');
        },
        remove: function () {
            $('#statusbar').css('background', 'none');
            $('#statusbar').removeClass('statusbar-red');
        }
    }
};

var copyObjectsManager = {
    init: function () {
        this._copyProgressObjects = {};
        this.infoMessage = '';
    },
    getInfoMessage: function () {
        return this._infoMessage;
    },
    setInfoMessage: function (message) {
        this._infoMessage = message;
    },
    addCopyStats: function (uri, loadedData, totalData) {

        uri = uri.substring(uri.indexOf('/vfs') + 5, uri.length); // slice uri to be prettier[ex: http://localhost:37911/api/vfs/ttesstt//Kudu.FunctionalTests/Vfs/VfsControllerTest.cs => ttesstt//Kudu.FunctionalTests/Vfs/VfsControllerTest.cs]
        if (this._copyProgressObjects[uri]) {
            if (loadedData === totalData) {
                this._copyProgressObjects[uri].endDate = $.now();
            } else {
                this._copyProgressObjects[uri].copyPackEnded = false;
            }
        } else {
            this._copyProgressObjects[uri] = {};
            this._copyProgressObjects[uri].startDate = $.now();
            this._copyProgressObjects[uri].copyPackEnded = false; //this is used for when copying multiple files in the same time so that i may still have a coherent percentage
        }

        if (totalData === 0) { // empty files appear to have size 0
            totalData = loadedData = 1;
        }

        this._copyProgressObjects[uri].loadedData = loadedData;
        this._copyProgressObjects[uri].totalData = totalData;
    },
    getCopyStats: function () {
        return this._copyProgressObjects;
    },
    getCurrentPercentCompletion: function () {
        var currentTransfered = 0;
        var finalTransfered = 0;
        var foundItem = false;

        for (var key in this._copyProgressObjects) {
            var co = this._copyProgressObjects[key];
            if (co.copyPackEnded === false) {
                foundItem = true;
                currentTransfered += co.loadedData;
                finalTransfered += co.totalData;
            }
        }

        var perc = 0;
        if (foundItem) {
            perc = parseInt((currentTransfered / finalTransfered) * 100);
        } else { // to avoid 0/0
            perc = 100;
        }

        if (perc === 100 && foundItem) { // if all transactions have finished & have some unmarked transaction pack, cancel it out
            for (var key in this._copyProgressObjects) {
                this._copyProgressObjects[key].copyPackEnded = true;
            }
        }

        return perc;
    },
    removeAtIndex: function (index) {
        delete this._copyProgressObjects[index];
    },
    clearData: function () {
        var date = new Date();
        this._infoMessage = 'You have cleared the cache at ' + date.toLocaleString();
        this._copyProgressObjects = {};
    }
}

copyObjectsManager.init();

function showErrorAsToast(error) {
    viewModel.processing(false);
    // Check if 'error' has a status property.
    // If true, treat as xhr response, otherwise string.
    if (error.status) {
        try {
            var message = JSON.parse(error.responseText).Message;
        }
        catch (e) {
            // error.responseText may be poisoned with HTML
            // (i.e. session expires and the 403 Forbidden response from App Service contains tons of markup)
            // Let's just ignore it if that's the case. We would need Cortana or something to parse that and
            // extract a meaningful message.
            if (!(/\<html\>/i.test(error.responseText))) {
                var message = error.responseText;
            }
        }
        var status = error.status;
        var statusText = error.statusText;
        var textToRender = status + ' ' + statusText + (typeof message !== 'undefined' ? ': ' + message : '');
        toast(textToRender);
    }
    // 'error' is a string
    else toast(error);
}

function toast(errorMsg) {
    var scaffold = '\
        <div class="row row-eq-height error notification">\
            <div id="toast-close" class="col-md-1">\
                <i class="glyphicon glyphicon-remove" aria-hidden="true"></i>\
            </div>\
            <div id="toast-msg" class="col-md-10">\
                <p><strong>ERROR</strong></p>' +
        errorMsg +
        '</div>\
        </div>';
    var item = $(scaffold);
    $('#toast').append($(item));
    $(item).animate({ 'right': '12px' }, 'fast');
    $('#toast').on('click', '#toast-close', function () {
        var notification = $(this).parent();
        notification.animate({ 'right': '-400px' }, function () {
            notification.remove();
        });
    });
}

function removeAllToasts() {
    $('.notification').remove();
}

function showhidepaKmanLoader(shVal) {
    if (shVal) {
        $('.paKman').show();
    }
    else {
        $('.paKman').hide();
    }
}

function progressBarDisplay(shVal) {
    if (shVal) {
        $('.btn.btn-info.box-border').show();
    }
    else {
        $('.btn.btn-info.box-border').hide();
    }
}

function showAceHelpModal() {
    $('#ace-help-modal').modal();
    //Can also be loaded from DebugConsole using path ~/Pages/filename
    $('#ace-help-modal .modal-body').load('/AceHelp.html',
        function (response, status, xhr) {
            if (status == 'error') {
                $(this).html('<div class="alert alert-warning" role="alert">' +
                    'Yikes! Can not load help page:<br>' +
                    'Error Code ' + xhr.status + ' ' + xhr.statusText + '</div>');
                if (typeof console == 'object') {
                    console.error('Can not load help page: ' + 'xhr.status = ' +
                        xhr.status + ' ' + xhr.statusText);
                }
            }
        });
}

$("#tblFileManager.table-responsive").hover(function () {
    $("#upload-widget-zip.dropzone.dz-clickable").css("background-color", "#f1f1f1");
    $("#upload-widget-zip.dropzone.dz-clickable").height($("#upload-widget.dropzone.dz-clickable").height());
}, function () {
    $("#upload-widget-zip.dropzone.dz-clickable").css("background-color", "");
});