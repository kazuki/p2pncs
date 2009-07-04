$(function() {
	var post_ajax = function () {
		var dlg = $("#dlgPost");
		var postName = encodeURIComponent ($("#postName").val());
		var postBody = encodeURIComponent ($("#postBody").val());
		var postToken = encodeURIComponent ($("#postToken").val());
		var postAnswer = encodeURIComponent ($("#postAnswer").val());
		var postPrev = encodeURIComponent ($("#postPrev").val());
		var bbsKey = $("#postKey").val();
		var postUrl = "/bbs/" + bbsKey + "?name=" + postName + "&body=" + postBody
			+ "&token=" + postToken + "&answer=" + postAnswer + "&prev=" + postPrev;
		dlg.dialog("option", "set_default") (dlg);
		$.ajax({
			dataType: "xml",
			cache: false,
			type: "POST",
			url: postUrl,
			error: function () {
				dlg.children().remove().add("p").text("CAPTCHA認証サーバへの問い合わせがタイムアウトしました");
				dlg.dialog("option", "buttons", {
					"OK": function() { $(this).dialog("option", "close2") ($(this)); }
				});
			},
			success: function (data) {
				switch ($("result:first", data).attr("status")) {
				case "CAPTCHA":
					$("#postToken").val ($("token:first", data).text());
					$("#postPrev").val ($("prev:first", data).text());
					$("#postToken").val ($("token:first", data).text());
					dlg.empty();
					$.create ("div",{},[
						"img", {src: "data:image/png;base64," + $("img:first", data).text()},[],
						"br",{},
						"input", {type:"text", id:"captchaAnswer"},[],
					]).appendTo(dlg);
					dlg.dialog("option", "buttons", {
						"送信": function() {
							$("#postAnswer").val ($("#captchaAnswer").val());
							post_ajax ();
						},
						"キャンセル": function() {
							$(this).dialog ("option", "close2") ($(this));
						}
					});
					return;
				case "OK":
					dlg.empty().add("p").text("投稿処理成功!");
					dlg.dialog("option", "buttons", {
						"OK": function() {
							$(this).dialog("option", "close2") ($(this));
							$("#postBody").val("");
							window.location.reload ();
						}
					});
					return;
				case "EMPTY":
					dlg.dialog("option", "close2") ($(this));
					return;
				default:
					dlg.empty().add("p").text("よくわかんないけど、エラーだよん");
					dlg.dialog("option", "buttons", {
						"OK": function() { $(this).dialog("option", "close2") ($(this)); }
					});
					return;
				}
			}
		});
	};
	$("#postButton").click(function() {
		var dlg = $.create("div", {title: "投稿中...", "id": "dlgPost"}, []).appendTo("body").dialog({
			autoOpen: true,
			modal: true,
			width: "auto",
			height: "auto",
			minHeight: 0,
			minWidth: 0,
			set_default: function (dialog) { dialog.empty().add("p").text("認証サーバへの問い合わせ中...");},
			beforeclose: function () { return false; },
			reset_hidden_fields: function () {
				$("#postToken").val("");
				$("#postAnswer").val("");
				$("#postPrev").val("");
			},
			close2: function (dlg) {
				dlg.dialog("option", "reset_hidden_fields") ();
				dlg.dialog("destroy").remove();
				alert ("OK");
			}
		});
		dlg.dialog("option", "reset_hidden_fields") ();
		post_ajax ();
	});
});
