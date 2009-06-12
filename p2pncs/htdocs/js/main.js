$(function() {
	var _rev = 0;
	var _joinDialogs = [];
	var _roomDialogs = [];

	function status2jp (status) {
		return status == "Establishing" ? "接続試行中"
			: status == "Unstable" ? "接続(不安定)"
			: status == "Stable" ? "接続"
			: "切断";
	}
	function start_basis_ajax () {
		var req_url = "/api?method=log&rev=" + _rev;
		$.ajax({
			type: "GET",
			url: req_url,
			dataType: "xml",
			success: basis_ajax_success,
			error: basis_ajax_error,
			timeout: 100 * 1000,
			cache: false
		});
	}
	function ajax_post_callback (data, textStatus) {
		if (data == "OK")
			return;
		$.create("div", {title: "エラー"}, [
			"p",{},[
				"span", {'class':"ui-icon ui-icon-alert", 'style':"float:left;margin-right:.5em"},[],
				data
			]
		]).appendTo("body").dialog({
			autoOpen: true,
			buttons: {
				OK: function() {$(this).dialog("close");}
			},
			close: function() { $(this).dialog("destroy").remove(); }
		});
	}
	function basis_ajax_success (data, textStatus) {
		var root = $(data).find("log:first");
		var array2 = _joinDialogs.slice(), array3 = [];
		_rev = $(root).attr("rev");
		
		$("#mainStatus").text ("(" + status2jp ($(root).attr("status")) + ")");
		$(root).find("joining-room").each (function() {
			var room_id = $(this).attr("id");
			var room_key = $(this).children("key:first").text();
			var joinDialog = $("#joinDialog" + room_id);
			array3.push([room_id, room_key]);
			if (joinDialog.size() == 0) {
				joinDialog = $.create("div", {title: "部屋へ接続中...", id: "joinDialog" + room_id}, [
					"div",{},[room_key + "へ接続中..."]
				]).appendTo("body").dialog({
					autoOpen: true,
					width: "auto",
					height: "auto",
					minHeight: 0,
					minWidth: 0,
					beforeclose: function () { return false; }
				});
			} else {
				for (var i = 0; i < array2.length; i ++) {
					if (array2[i][0] == room_id && array2[i][1] == room_key) {
						array2.splice (i, 1);
						break;
					}
				}
			}
		});
		_joinDialogs = array3;
		for (var i = 0; i < array2.length; i ++) {
			var dlg = $("#joinDialog" + array2[i][0]);
			var joined = $(root).find("room[id=", array2[i][0] + "] > key");
			if (joined.size() > 0 && joined.text() == array2[i][1]) {
				dlg.dialog("destroy").remove();
				continue;
			}
			dlg.children().text(array2[i][1] + "への接続に失敗しました");
			dlg.dialog("option", "buttons", {
				"OK": function() { $(this).dialog("destroy").remove(); }
			});
		}

		array2 = _roomDialogs.slice();
		array3 = [];
		$(root).find("room").each (function() {
			var room_id = $(this).attr("id");
			var room_owner = $(this).attr("owner");
			var room_status = $(this).attr("status");
			var room_name = $(this).children("name:first").text();
			var room_key = $(this).children("key:first").text();
			var roomDialog = $("#roomDialog" + room_id);
			var joinDialog = $("#joinDialog" + room_id);
			if (joinDialog.size() != 0)
				joinDialog.dialog("destroy").remove();
			array3.push([room_id, room_key]);
			if (roomDialog.size() == 0) {
				roomDialog = $.create("div", {"title": room_name, "id": "roomDialog" + room_id}, [
					"div",{"class":"chat-content"},[],
					"div",{"class":"chat-post"},[
						"input",{"style":"width:99%"},[]
					],
					"div",{"class":"chat-statusbar"},[
						"span",{"class":"chat-status"},["状態:不明"],
						"span",{"class":"chat-roomid"},["ルームID: ", room_key]
					]
				]).appendTo("body").dialog({
					autoOpen: true,
					width: 640,
					height: 480,
					resize: function () {
						$(this).dialog("option", "resize_internal") (this);
					},
					resize_internal: function(this2) {
						var target = $(this2).children("div.chat-content");
						var parent = $(target).parent();
						var others_height = 0;
						$(parent).children("div:not(.chat-content)").each(function() {
							others_height += $(this).outerHeight({margin: true});
						});
						target.height($(parent).height() - others_height);
					},
					open: function() {
						$(this).dialog("option", "resize_internal") ($(this));
					},
					update_values: function(this2, status) {
						status_jp = status2jp (status);
						$(this2).find("span.chat-status").text("状態:" + status_jp);
					},
					update_logs: function (this2, posts) {
						var content = $(this2).children("div.chat-content");
						$(posts).each (function () {
							if (content.children().size() > 0)
								$.create("hr",{},[]).appendTo(content);
							$.create("div",{},[
								$(this).children("name").text() + "> " + $(this).children("message").text()
							]).appendTo(content);
						});
						$(content).scrollTop ($(content).get(0).scrollHeight);
					},
					beforeclose: function () {
						var msg;
						var this2 = $(this);
						if (room_owner == "true")
							msg = ["あなたは" + room_name + "のオーナーですが、閉じてもよろしいですか?"];
						else
							msg = [room_name + "から退室してもよろしいですか?"];
						$.create("div", {title: "退室確認"}, ["div",{},msg]).appendTo("body").dialog({
							autoOpen: true,
							buttons: {
								"OK": function () {
									for (var i = 0; i < _roomDialogs.length; i ++) {
										if (_roomDialogs[i][0] == room_id && _roomDialogs[i][1] == room_key) {
											_roomDialogs.splice (i, 1);
											break;
										}
									}
									$.post("/api?method=leave_room&id=" + room_id, {}, ajax_post_callback, "text");
									$(this2).dialog("destroy").remove();
									$(this).dialog("close");
								},
								"キャンセル": function () { $(this).dialog("close"); }
							},
							close: function () { $(this).dialog("destroy").remove(); }
						});
						return false;
					}
				});
				$(roomDialog).find("div.chat-post > input").keydown (function (e) {
					if (e.keyCode == 13 && $(this).val().length > 0) {
						var postdata = encodeURIComponent ($(this).val());
						$.post("/api?method=post&id=" + room_id + "&msg=" + postdata, {}, ajax_post_callback, "text");
						$(this).val("");
						return false;
					}
					return true;
				});
			} else {
				for (var i = 0; i < array2.length; i ++) {
					if (array2[i][0] == room_id && array2[i][1] == room_key) {
						array2.splice (i, 1);
						break;
					}
				}
			}
			$(roomDialog).dialog("option", "update_values") (roomDialog, room_status);
			$(roomDialog).dialog("option", "update_logs") (roomDialog, $(this).children("post"));
		});
		for (var i = 0; i < array2.length; i ++) {
			$("#roomDialog" + array2[i][0]).dialog("destroy").remove();
		}
		_roomDialogs = array3;

		window.setTimeout (start_basis_ajax, 0);
	}
	function basis_ajax_error (data, textStatus) {
		window.setTimeout (start_basis_ajax, 10 * 1000);
	}

	$("#navigation").accordion({
		autoHeight: false
	});
	$("#connect_dialog").click(function() {
		$.create("div", {"title": "初期ノードに接続"}, [
			"p",{},["初期ノードの情報を入力してください"],
			"input",{},[]
		]).appendTo("body").dialog({
			autoOpen: true,
			modal: false,
			minHeight: 0,
			width: "auto",
			resizable: false,
			buttons: {
				"接続": function () {
					var postdata = encodeURIComponent ($(this).children("input:first").val());
					$.post("/api?method=connect&data=" + postdata, {}, ajax_post_callback, "text");
					$(this).dialog("close");
				},
				"キャンセル": function () {
					$(this).dialog("close");
				}
			},
			close: function() {
				$(this).dialog("destroy").remove ();
			}
		});
	});
	$("#exit_dialog").click(function() {
		$.create("div", {"title": "終了確認"}, [
			"p",{},["プログラムを終了してもよろしいですか？"]
		]).appendTo("body").dialog({
			autoOpen: true,
			modal: true,
			minHeight: 0,
			width: "auto",
			resizable: false,
			buttons: {
				"終了": function() {
					$.post("/api?method=exit", {}, ajax_post_callback, "text");
					$(this).dialog("close");
				},
				"キャンセル": function() {
					$(this).dialog("close");
				}
			},
			close: function() {
				$(this).dialog("destroy").remove();
			}
		});
	});
	$("#create_room_dialog").click(function() {
		$.create("div", {"title": "部屋の作成"}, [
			"p",{},["部屋の名前:"],
			"input",{},[]
		]).appendTo("body").dialog({
			autoOpen: true,
			minHeight: 0,
			width: "auto",
			resizable: false,
			buttons: {
				"作成": function() {
					var postdata = encodeURIComponent ($(this).children("input:first").val());
					$.post("/api?method=create_room&data=" + postdata, {}, ajax_post_callback, "text");
					$(this).dialog("close");
				},
				"キャンセル": function() {
					$(this).dialog("close");
				}
			},
			close: function() {
				$(this).dialog("destroy").remove();
			}
		});
	});
	$("#join_dialog").click(function() {
		$.create("div", {"title": "部屋に接続"}, [
			"p",{},["部屋のID:"],
			"input",{"size": 48},[]
		]).appendTo("body").dialog({
			autoOpen: true,
			minHeight: 0,
			width: "auto",
			resizable: false,
			buttons: {
				"接続": function() {
					var postdata = encodeURIComponent ($(this).children("input:first").val());
					$.post("/api?method=join_room&data=" + postdata, {}, ajax_post_callback, "text");
					$(this).dialog("close");
				},
				"キャンセル": function() {
					$(this).dialog("close");
				}
			},
			close: function() {
				$(this).dialog("destroy").remove();
			}
		});
	});
	$("#throughput_test_dialog").click(function() {
		$.create("div", {"title": "スループット測定"}, [
			"p",{},["相手のID:"],
			"input",{"size": 48},[],
			"p",{},["(結果は全てコマンドプロンプトの方に表示されます)"]
		]).appendTo("body").dialog({
			autoOpen: true,
			minHeight: 0,
			width: "auto",
			resizable: false,
			buttons: {
				"測定": function() {
					var postdata = encodeURIComponent ($(this).children("input:first").val());
					$.post("/api?method=throughput_test&data=" + postdata, {}, ajax_post_callback, "text");
					$(this).dialog("close");
				},
				"キャンセル": function() {
					$(this).dialog("close");
				}
			},
			close: function() {
				$(this).dialog("destroy").remove();
			}
		});
	});

	start_basis_ajax ();
});
