$(function() {
	$("#postButton").click(function() {
		var postName = encodeURIComponent ($("#postName").val());
		var postBody = encodeURIComponent ($("#postBody").val());
		var postToken = encodeURIComponent ($("#postToken").val());
		var postAnswer = encodeURIComponent ($("#postAnswer").val());
		var postPrev = encodeURIComponent ($("#postPrev").val());
		var bbsKey = $("#postKey").val();
		var postUrl = "/bbs/" + bbsKey + "?name=" + postName + "&body=" + postBody
			+ "&token=" + postToken + "&answer=" + postAnswer + "&prev=" + postPrev;
		var dlg = $.create("div", {title: "投稿中..."}, ["投稿中..."]).appendTo("body").dialog({
			autoOpen: true,
			modal: true,
			width: "auto",
			height: "auto",
			minHeight: 0,
			minWidth: 0,
			beforeclose: function () { return false; }
		});
		$.ajax({
			dataType: "xml",
			cache: false,
			type: "POST",
			url: postUrl,
			error: function () {
				dlg.children().remove().add("p").text("CAPTCHA認証サーバへの問い合わせがタイムアウトしました");
				dlg.dialog("option", "buttons", {
					"OK": function() { $(this).dialog("destroy").remove(); }
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
							dlg.dialog("destroy").remove();
							$("#postButton").click ();
						}
					});
					return;
				case "OK":
					dlg.empty().add("p").text("投稿処理成功!");
					dlg.dialog("option", "buttons", {
						"OK": function() {
							$(this).dialog("destroy").remove();
							$("#postBody").val("");
							window.location.reload ();
						}
					});
					return;
				case "EMPTY":
					dlg.dialog("destroy").remove();
				default:
					dlg.empty().add("p").text("よくわかんないけど、エラーだよん");
					dlg.dialog("option", "buttons", {
						"OK": function() { $(this).dialog("destroy").remove(); }
					});
				}
			}});
	});
});
