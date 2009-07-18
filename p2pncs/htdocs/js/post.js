$.extend({
	create_post_dialog: function(postUrl, postData, success_callback, failure_callback) {
		postData["token"] = "";
		postData["answer"] = "";
		postData["prev"] = "";
		var dlg = $.create("div", {title: "投稿中...", "id": "dlgPost"}, []).appendTo("body").dialog({
			autoOpen: true,
			modal: true,
			width: "auto",
			height: "auto",
			minHeight: 0,
			minWidth: 0,
			beforeclose: function () { return false; }
		});
		dlg.extend({
			set_text: function (text) {
				this.empty().append (text);
			},
			set_default: function () {
				this.set_text ("認証サーバへの問い合わせ中...");
			},
			close_force: function (is_success) {
				if (is_success)
					success_callback ();
				else
					failure_callback ();
				dlg.dialog("destroy").remove();
			}
		});

		var post_ajax = function () {
			dlg.set_default ();
			$.ajax({
				dataType: "xml",
				cache: false,
				type: "POST",
				url: postUrl,
				data: postData,
				error: function () {
					dlg.set_text ("CAPTCHA認証サーバへの問い合わせがタイムアウトしました");
					dlg.dialog("option", "buttons", {
						"OK": function() { dlg.close_force (false); }
					});
				},
				success: function (data) {
					switch ($("result:first", data).attr("status")) {
					case "CAPTCHA":
						postData["token"] = $("token:first", data).text();
						postData["prev"] = $("prev:first", data).text();
						dlg.empty();
						$.create ("div",{},[
							"img", {src: "data:image/png;base64," + $("img:first", data).text()},[],
							"br",{},
							"input", {type:"text", id:"captchaAnswer"},[],
						]).appendTo(dlg);
						dlg.dialog("option", "buttons", {
							"送信": function() {
							postData["answer"] = $("#captchaAnswer").val();
								dlg.dialog("option", "buttons", {});
								post_ajax ();
							},
							"キャンセル": function() {dlg.close_force (false);}
						});
						return;
					case "OK":
						dlg.set_text ("投稿処理成功!");
						dlg.dialog("option", "buttons", {
							"OK": function() {dlg.close_force (true);}
						});
						return;
					case "EMPTY":
						dlg.close_force(false);
						return;
					case "ERROR":
						dlg.set_text ($("result:first", data).text ());
						dlg.dialog("option", "buttons", {
							"OK": function() {dlg.close_force(false);}
						});
						return;
					default:
						dlg.set_text ("よくわかんないけど、エラーだよん");
						dlg.dialog("option", "buttons", {
							"OK": function() {dlg.close_force(false);}
						});
						return;
					}
				}
			});
		};
		post_ajax ();
	}
});
