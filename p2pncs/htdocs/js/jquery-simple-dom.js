/*
 * Simple DOM Utilities for jQuery
 * Copyright(C) 2009 Kazuki Oikawa
 * Dual licensed under the MIT and GPL licenses.
 *
 * Example:
 *   $.create('div',{style:'font-weight:bold'},[
 *     'div',{},['Hello World','br',{},'Test Message'],
 *     'div',{},[
 *       'ul',{},[
 *         'li',{},['Item 1'],
 *         'li',{},['Item 2'],
 *         'li',{},['Item 3']
 *       ]
 *     ]
 *   ]);
 *
 *  output:
 *    <div style="font-weight:bold">
 *      <div>Hello World<br/>Test Message</div>
 *      <div>
 *        <ul>
 *          <li>Item 1</li>
 *          <li>Item 2</li>
 *          <li>Item 3</li>
 *        </ul>
 *      </div>
 *    </div>
*/
$.extend({
	createText: function(txt) {
		return $(document.createTextNode(txt));
	},
	create: function() {
		function isString (obj) {
			return typeof(obj) == "string" || obj instanceof String;
		}
		var ret = [];
		var args = arguments;
		if (!$.isArray(args))
			args = $.makeArray(args);
		if (args.length == 1 && $.isArray(args[0]))
			args = args[0];
		if (args.length == 0)
			return ret;
		if (args.length == 1 || isString(args[1])) {
			ret.push ($.createText(args[0]));
			if (args.length > 1) {
				var tmp = $.create (args.slice(1));
				for (var i = 0; i < tmp.length; i ++)
					ret.push (tmp[i]);
			}
		} else {
			var element = $(document.createElement (args[0]));
			for (var attr in args[1]) {
				if (attr == "toggle" && $.isArray(args[1][attr])) {
					fs = args[1][attr];
					switch (fs.length) {
					case 1: element.toggle (fs[0]); break;
					case 2: element.toggle (fs[0], fs[1]); break;
					case 3: element.toggle (fs[0], fs[1], fs[2]); break;
					case 4: element.toggle (fs[0], fs[1], fs[2], fs[3]); break;
					default: throw "Not supported";
					}
				} else if ($.isFunction (args[1][attr])) {
					element.bind (attr, args[1][attr]);
				} else {
					element.attr (attr, args[1][attr]);
				}
			}
			ret.push (element);
			var idx = 3;
			if (args.length > 2) {
				if ($.isArray(args[2])) {
					var tmp = $.create(args[2]);
					for (var i = 0; i < tmp.length; i ++)
						element.append (tmp[i]);
				} else {
					idx = 2;
				}
			}
			if (args.length >= idx) {
				tmp = $.create(args.slice(idx));
				for (var i = 0; i < tmp.length; i ++)
					ret.push (tmp[i]);
			}
		}
		if (arguments.length == 3)
			return ret[0];
		return ret;
	}
});
