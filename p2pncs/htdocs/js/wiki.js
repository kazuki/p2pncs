$(function() {
	$("#postButton").click(function() {
		$.create_post_dialog ("/wiki/" + $("#postKey").val() + "/", {
			"name": $("#postName").val(),
			"body": $("#postBody").val(),
			"title": $("#postPage").val(),
			"auth": $("#authsvr").val(),
			"lzma": $("#use_lzma").val(),
			"parent": $("#parentHash").val()
		}, function () {
			window.location = "/wiki/" + $("#postKey").val() + "/" + encodeURIComponent ($("#postPage").val());
		}, function () {});
		return false;
	});
});
